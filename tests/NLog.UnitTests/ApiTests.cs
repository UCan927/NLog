// 
// Copyright (c) 2004-2021 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

using System.Linq;

namespace NLog.UnitTests
{
    using System;
    using System.Text;
    using System.Collections.Generic;
    using System.Reflection;
    using NLog.Config;
    using Xunit;

    /// <summary>
    /// Test the characteristics of the API. Config of the API is tested in <see cref="NLog.UnitTests.Config.ConfigApiTests"/>
    /// </summary>
    public class ApiTests : NLogTestBase
    {
        private Type[] allTypes;
        private Assembly nlogAssembly = typeof(LogManager).Assembly;
        private readonly Dictionary<Type, int> typeUsageCount = new Dictionary<Type, int>();

        public ApiTests()
        {
            allTypes = typeof(LogManager).Assembly.GetTypes();
        }

        [Fact]
        public void PublicEnumsTest()
        {
            foreach (Type type in allTypes)
            {
                if (!type.IsPublic)
                {
                    continue;
                }

                if (type.IsEnum || type.IsInterface)
                {
                    typeUsageCount[type] = 0;
                }
            }

            typeUsageCount[typeof(IInstallable)] = 1;

            foreach (Type type in allTypes)
            {
                if (type.IsGenericTypeDefinition)
                {
                    continue;
                }

                if (type.BaseType != null)
                {
                    IncrementUsageCount(type.BaseType);
                }

                foreach (var iface in type.GetInterfaces())
                {
                    IncrementUsageCount(iface);
                }

                foreach (var method in type.GetMethods())
                {
                    if (method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    // Console.WriteLine("  {0}", method.Name);
                    try
                    {
                        IncrementUsageCount(method.ReturnType);

                        foreach (var p in method.GetParameters())
                        {
                            IncrementUsageCount(p.ParameterType);
                        }
                    }
                    catch (Exception ex)
                    {
                        // this sometimes throws on .NET Compact Framework, but is not fatal
                        Console.WriteLine("EXCEPTION {0}", ex);
                    }
                }
            }

            var unusedTypes = new List<Type>();
            StringBuilder sb = new StringBuilder();

            foreach (var kvp in typeUsageCount)
            {
                if (kvp.Value == 0)
                {
                    Console.WriteLine("Type '{0}' is not used.", kvp.Key);
                    unusedTypes.Add(kvp.Key);
                    sb.Append(kvp.Key.FullName).Append("\n");
                }
            }

            Assert.Empty(unusedTypes);
        }

        [Fact]
        public void TypesInInternalNamespaceShouldBeInternalTest()
        {
            var excludes = new HashSet<Type>
            {
                typeof(NLog.Internal.Xamarin.PreserveAttribute),
#pragma warning disable CS0618 // Type or member is obsolete
                typeof(NLog.Internal.Fakeables.IAppDomain), // TODO NLog 5 - handle IAppDomain
#pragma warning restore CS0618 // Type or member is obsolete
            };

            var notInternalTypes = allTypes
                .Where(t => t.Namespace != null && t.Namespace.Contains(".Internal"))
                .Where(t => !t.IsNested && (t.IsVisible || t.IsPublic))
                .Where(n => !excludes.Contains(n))
                .Select(t => t.FullName)
                .ToList();

            Assert.Empty(notInternalTypes);
        }

        private void IncrementUsageCount(Type type)
        {
            if (type.IsArray)
            {
                type = type.GetElementType();
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                IncrementUsageCount(type.GetGenericTypeDefinition());
                foreach (var parm in type.GetGenericArguments())
                {
                    IncrementUsageCount(parm);
                }
                return;
            }

            if (type.Assembly != nlogAssembly)
            {
                return;
            }

            if (typeUsageCount.ContainsKey(type))
            {
                typeUsageCount[type]++;
            }
        }

        [Fact]
        public void TryGetRawValue_ThreadAgnostic_Attribute_Required()
        {
            foreach (Type type in allTypes)
            {
                if (typeof(NLog.Internal.IRawValue).IsAssignableFrom(type) && !type.IsInterface)
                {
                    var threadAgnosticAttribute = type.GetCustomAttribute<ThreadAgnosticAttribute>();
                    Assert.True(!(threadAgnosticAttribute is null), $"{type.ToString()} cannot implement IRawValue");
                }
            }
        }

        [Fact]
        public void IStringValueRenderer_AppDomainFixedOutput_Attribute_NotRequired()
        {
            foreach (Type type in allTypes)
            {
                if (typeof(NLog.Internal.IStringValueRenderer).IsAssignableFrom(type) && !type.IsInterface)
                {
                    var appDomainFixedOutputAttribute = type.GetCustomAttribute<AppDomainFixedOutputAttribute>();
                    Assert.True(appDomainFixedOutputAttribute is null, $"{type.ToString()} should not implement IStringValueRenderer");
                }
            }
        }

        [Fact]
        public void RequiredConfigOptionMustBeClass()
        {
            foreach (Type type in allTypes)
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var prop in properties)
                {
                    var requiredParameter = prop.GetCustomAttribute<NLog.Config.RequiredParameterAttribute>();
                    if (requiredParameter != null)
                    {
                        Assert.True(prop.PropertyType.IsClass, type.Name);
                    }
                }
            }
        }

        [Fact]
        public void SingleDefaultConfigOption()
        {
            string prevDefaultPropertyName = null;

            foreach (Type type in allTypes)
            {
                prevDefaultPropertyName = null;

                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var prop in properties)
                {
                    var defaultParameter = prop.GetCustomAttribute<DefaultParameterAttribute>();
                    if (defaultParameter != null)
                    {
                        Assert.True(prevDefaultPropertyName == null, prevDefaultPropertyName?.ToString());
                        prevDefaultPropertyName = prop.Name;
                        Assert.True(type.IsSubclassOf(typeof(NLog.LayoutRenderers.LayoutRenderer)), type.ToString());
                    }
                }
            }
        }

        [Fact]
        public void AppDomainFixedOutput_Attribute_EnsureThreadAgnostic()
        {
            foreach (Type type in allTypes)
            {
                var appDomainFixedOutputAttribute = type.GetCustomAttribute<AppDomainFixedOutputAttribute>();
                if (appDomainFixedOutputAttribute != null)
                {
                    var threadAgnosticAttribute = type.GetCustomAttribute<ThreadAgnosticAttribute>();
                    Assert.True(!(threadAgnosticAttribute is null), $"{type.ToString()} should also have ThreadAgnostic");
                }
            }
        }

        [Fact]
        public void ValidateLayoutRendererTypeAlias()
        {
            // These class-names should be repaired with next major version bump
            // Do NOT add more incorrect class-names to this exlusion-list
            HashSet<string> oldFaultyClassNames = new HashSet<string>()
            {
                "GarbageCollectorInfoLayoutRenderer",
                "ScopeContextNestedStatesLayoutRenderer",
                "ScopeContextPropertyLayoutRenderer",
                "ScopeContextTimingLayoutRenderer",
                "TraceActivityIdLayoutRenderer",
                "SpecialFolderApplicationDataLayoutRenderer",
                "SpecialFolderCommonApplicationDataLayoutRenderer",
                "SpecialFolderLocalApplicationDataLayoutRenderer",
                "DirectorySeparatorLayoutRenderer",
                "LiteralWithRawValueLayoutRenderer",
                "LocalIpAddressLayoutRenderer",
                "VariableLayoutRenderer",
                "ObjectPathRendererWrapper",
                "PaddingLayoutRendererWrapper",
            };

            foreach (Type type in allTypes)
            {
                if (type.IsSubclassOf(typeof(NLog.LayoutRenderers.LayoutRenderer)))
                {
                    var layoutRendererAttributes = type.GetCustomAttributes<NLog.LayoutRenderers.LayoutRendererAttribute>()?.ToArray() ?? new NLog.LayoutRenderers.LayoutRendererAttribute[0];
                    if (layoutRendererAttributes.Length == 0)
                    {
                        if (type != typeof(NLog.LayoutRenderers.FuncLayoutRenderer) && type != typeof(NLog.LayoutRenderers.FuncThreadAgnosticLayoutRenderer))
                        {
                            Assert.True(type.IsAbstract, $"{type} without LayoutRendererAttribute must be abstract");
                        }
                    }
                    else
                    {
                        Assert.False(type.IsAbstract, $"{type} with LayoutRendererAttribute cannot be abstract");

                        if (!oldFaultyClassNames.Contains(type.Name))
                        {
                            if (type.IsSubclassOf(typeof(NLog.LayoutRenderers.Wrappers.WrapperLayoutRendererBase)))
                            {
                                var typeAlias = layoutRendererAttributes.First().Name.Replace("-", "");
                                Assert.Equal(typeAlias + "LayoutRendererWrapper", type.Name, StringComparer.OrdinalIgnoreCase);
                            }
                            else
                            {
                                var typeAlias = layoutRendererAttributes.First().Name.Replace("-", "");
                                Assert.Equal(typeAlias + "LayoutRenderer", type.Name, StringComparer.OrdinalIgnoreCase);
                            }
                        }
                    }
                }
            }
        }
    }
}