﻿// Copyright (c) 2021-2022 Ubisoft Entertainment
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sharpmake.Generators;
using Sharpmake.Generators.VisualStudio;

namespace Sharpmake
{
    public static partial class Android
    {
        [PlatformImplementation(Platform.agde,
            typeof(IPlatformDescriptor),
            typeof(IPlatformVcxproj),
            typeof(Project.Configuration.IConfigurationTasks))]
        public sealed partial class AndroidAgdePlatform : BasePlatform, Project.Configuration.IConfigurationTasks
        {
            #region IPlatformDescriptor implementation.
            public override string SimplePlatformString => "Android";
            public override string GetPlatformString(ITarget target)
            {
                if (target == null)
                    return SimplePlatformString;

                var buildTarget = target.GetFragment<AndroidBuildTargets>();
                switch (buildTarget)
                {
                    case AndroidBuildTargets.armeabi_v7a:
                        return "Android-armeabi-v7a";
                    case AndroidBuildTargets.arm64_v8a:
                        return "Android-arm64-v8a";
                    case AndroidBuildTargets.x86:
                        return "Android-x86";
                    case AndroidBuildTargets.x86_64:
                        return "Android-x86_64";
                    default:
                        throw new System.Exception(string.Format("Unsupported Android architecture: {0}", buildTarget));
                }
            }

            public override bool IsMicrosoftPlatform => false;
            public override bool IsPcPlatform => false;
            public override bool IsUsingClang => true;
            public override bool IsLinkerInvokedViaCompiler { get; set; } = true;
            public override bool HasDotNetSupport => false;
            public override bool HasSharedLibrarySupport => true;
            public override bool HasPrecompiledHeaderSupport => true;
            #endregion

            #region Project.Configuration.IConfigurationTasks implementation
            public void SetupDynamicLibraryPaths(Project.Configuration configuration, DependencySetting dependencySetting, Project.Configuration dependency)
            {
                // Not tested. We may need to root the path like we do in SetupStaticLibraryPaths.
                DefaultPlatform.SetupLibraryPaths(configuration, dependencySetting, dependency);
            }

            public void SetupStaticLibraryPaths(Project.Configuration configuration, DependencySetting dependencySetting, Project.Configuration dependency)
            {
                DefaultPlatform.SetupLibraryPaths(configuration, dependencySetting, dependency);
            }

            // The below method was replaced by GetDefaultOutputFullExtension
            // string GetDefaultOutputExtension(OutputType outputType);

            public string GetDefaultOutputFullExtension(Project.Configuration.OutputType outputType)
            {
                switch (outputType)
                {
                    case Project.Configuration.OutputType.Exe:
                        return ExecutableFileFullExtension;
                    case Project.Configuration.OutputType.Dll:
                        return SharedLibraryFileFullExtension;
                    default:
                        return StaticLibraryFileFullExtension;
                }
            }

            public string GetOutputFileNamePrefix(Project.Configuration.OutputType outputType)
            {
                switch (outputType)
                {
                    case Project.Configuration.OutputType.Exe:
                    case Project.Configuration.OutputType.Dll:
                    case Project.Configuration.OutputType.Lib:
                        return "lib";
                    default:
                        return string.Empty;
                }
            }

            public IEnumerable<string> GetPlatformLibraryPaths(Project.Configuration configuration)
            {
                yield break;
            }
            #endregion

            #region IPlatformVcxproj implementation
            public override string ProgramDatabaseFileFullExtension => string.Empty;
            public override string SharedLibraryFileFullExtension => ".so";
            public override string StaticLibraryFileFullExtension => ".a";
            public override string ExecutableFileFullExtension => ".so";

            public override void GeneratePlatformSpecificProjectDescription(IVcxprojGenerationContext context, IFileGenerator generator)
            {
                generator.Write(_projectStartPlatformConditional);

                string msBuildPathOverrides = string.Empty;

                // MSBuild override when mixing devenvs in the same vcxproj is not supported,
                // but before throwing an exception check if we have some override
                for (DevEnv devEnv = context.DevelopmentEnvironmentsRange.MinDevEnv; devEnv <= context.DevelopmentEnvironmentsRange.MaxDevEnv; devEnv = (DevEnv)((int)devEnv << 1))
                {
                    switch (devEnv)
                    {
                        case DevEnv.vs2017:
                        case DevEnv.vs2019:
                        case DevEnv.vs2022:
                            {
                                // _PlatformFolder override is not enough for android, we need to know the AdditionalVCTargetsPath
                                // Note that AdditionalVCTargetsPath is not officially supported by vs2017, but we use the variable anyway for convenience and consistency
                                if (!string.IsNullOrEmpty(MSBuildGlobalSettings.GetCppPlatformFolder(devEnv, Platform.agde)))
                                    throw new Error($"SetCppPlatformFolder is not supported by {devEnv}: use of MSBuildGlobalSettings.SetCppPlatformFolder should be replaced by use of MSBuildGlobalSettings.SetAdditionalVCTargetsPath.");

                                string additionalVCTargetsPath = MSBuildGlobalSettings.GetAdditionalVCTargetsPath(devEnv, Platform.agde);
                                if (!string.IsNullOrEmpty(additionalVCTargetsPath))
                                {
                                    using (generator.Declare("additionalVCTargetsPath", Sharpmake.Util.EnsureTrailingSeparator(additionalVCTargetsPath)))
                                        msBuildPathOverrides += generator.Resolver.Resolve(Vcxproj.Template.Project.AdditionalVCTargetsPath);
                                }
                            }
                            break;
                    }
                }

                var agdeConfOptions = context.ProjectConfigurationOptions.Where(d => d.Key.Platform == Platform.agde).Select(d => d.Value);

                using (generator.Declare("androidHome", Options.GetOptionValue("androidHome", agdeConfOptions)))
                using (generator.Declare("javaHome", Options.GetOptionValue("javaHome", agdeConfOptions)))
                using (generator.Declare("androidNdkVersion", Options.GetOptionValue("androidNdkVersion", agdeConfOptions)))
                using (generator.Declare("androidMinSdkVersion", Options.GetOptionValue("androidMinSdkVersion", agdeConfOptions)))
                using (generator.Declare("ndkRoot", Options.GetOptionValue("ndkRoot", agdeConfOptions)))
                {
                    generator.Write(_projectDescriptionPlatformSpecific);
                }

                if (!string.IsNullOrEmpty(msBuildPathOverrides))
                {
                    if (context.DevelopmentEnvironmentsRange.MinDevEnv != context.DevelopmentEnvironmentsRange.MaxDevEnv)
                        throw new Error("Different vs versions not supported in the same vcxproj");

                    generator.WriteVerbatim(msBuildPathOverrides);
                }

                generator.Write(Vcxproj.Template.Project.ProjectDescriptionEnd);
            }

            public override void GenerateProjectPlatformSdkDirectoryDescription(IVcxprojGenerationContext context, IFileGenerator generator)
            {
                base.GenerateProjectPlatformSdkDirectoryDescription(context, generator);

                var devEnv = context.DevelopmentEnvironmentsRange.MinDevEnv;
                if (devEnv.IsVisualStudio() && devEnv >= DevEnv.vs2019)
                {
                    string additionalVCTargetsPath = MSBuildGlobalSettings.GetAdditionalVCTargetsPath(devEnv, Platform.agde);
                    if (!string.IsNullOrEmpty(additionalVCTargetsPath))
                        generator.WriteVerbatim(_projectPropertySheets);
                }
            }

            public override void GenerateProjectCompileVcxproj(IVcxprojGenerationContext context, IFileGenerator generator)
            {
                generator.Write(_projectConfigurationsCompileTemplate);
            }

            public override void GenerateProjectConfigurationGeneral(IVcxprojGenerationContext context, IFileGenerator generator)
            {
                generator.Write(_projectConfigurationsGeneralTemplate);
            }

            public override void GenerateProjectConfigurationGeneral2(IVcxprojGenerationContext context, IFileGenerator generator)
            {
                generator.Write(_projectConfigurationsGeneral2Template);
            }

            protected override string GetProjectLinkSharedVcxprojTemplate()
            {
                return _projectConfigurationsSharedLinkTemplate;
            }

            protected override string GetProjectStaticLinkVcxprojTemplate()
            {
                return _projectConfigurationsStaticLinkTemplate;
            }

            public override void SetupSdkOptions(IGenerationContext context)
            {
                base.SetupSdkOptions(context);
                var conf = context.Configuration;
                var options = context.Options;

                options["androidHome"] = Options.PathOption.Get<Options.Android.General.AndroidHome>(conf, GlobalSettings.AndroidHome ?? RemoveLineTag, context.ProjectDirectoryCapitalized);
                options["ndkRoot"] = Options.PathOption.Get<Options.Android.General.NdkRoot>(conf, GlobalSettings.NdkRoot ?? RemoveLineTag, context.ProjectDirectoryCapitalized);
                options["javaHome"] = Options.PathOption.Get<Options.Android.General.JavaHome>(conf, GlobalSettings.JavaHome ?? RemoveLineTag, context.ProjectDirectoryCapitalized);

                string ndkRoot = options["ndkRoot"].Equals(RemoveLineTag) ? null : options["ndkRoot"];
                string ndkVer = Util.GetNdkVersion(ndkRoot);
                options["androidNdkVersion"] = ndkVer.Equals(string.Empty) ? RemoveLineTag : ndkVer;

                var sdkIncludePaths = GetSdkIncludePaths(context);
                options["IncludePath"] = sdkIncludePaths.JoinStrings(";");
            }

            public override void SelectCompilerOptions(IGenerationContext context)
            {
                base.SelectCompilerOptions(context);

                var options = context.Options;
                var cmdLineOptions = context.CommandLineOptions;
                var conf = context.Configuration;

                if (conf.Output.Equals(Project.Configuration.OutputType.Exe))
                {
                    options["AndroidEnablePackaging"] = "true";
                    string option = Options.StringOption.Get<Options.Agde.General.AndroidApplicationModule>(conf);
                    options["AndroidApplicationModule"] = option != RemoveLineTag ? option : context.Project.Name.ToLowerInvariant();

                    options["AndroidGradleBuildDir"] = Options.PathOption.Get<Options.Android.General.AndroidGradleBuildDir>(conf, @"$(SolutionDir)");
                    options["AndroidGradleBuildIntermediateDir"] = Options.PathOption.Get<Options.Agde.General.AndroidGradleBuildIntermediateDir>(conf);
                    options["AndroidExtraGradleArgs"] = Options.StringOption.Get<Options.Agde.General.AndroidExtraGradleArgs>(conf);

                    option = Options.StringOption.Get<Options.Android.General.AndroidApkName>(conf);
                    options["AndroidApkName"] = option != RemoveLineTag ? option : @"$(RootNamespace)-$(PlatformTarget).apk";
                }
                else
                {
                    options["AndroidEnablePackaging"] = RemoveLineTag;
                    options["AndroidApplicationModule"] = RemoveLineTag;
                    options["AndroidGradleBuildDir"] = RemoveLineTag;
                    options["AndroidGradleBuildIntermediateDir"] = RemoveLineTag;
                    options["AndroidExtraGradleArgs"] = RemoveLineTag;
                    options["AndroidApkName"] = RemoveLineTag;
                }

                context.SelectOption
                (
                Options.Option(Options.Android.General.ShowAndroidPathsVerbosity.Default, () => { options["ShowAndroidPathsVerbosity"] = RemoveLineTag; }),
                Options.Option(Options.Android.General.ShowAndroidPathsVerbosity.High, () => { options["ShowAndroidPathsVerbosity"] = "High"; }),
                Options.Option(Options.Android.General.ShowAndroidPathsVerbosity.Normal, () => { options["ShowAndroidPathsVerbosity"] = "Normal"; }),
                Options.Option(Options.Android.General.ShowAndroidPathsVerbosity.Low, () => { options["ShowAndroidPathsVerbosity"] = "Low"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.General.AndroidAPILevel.Latest, () =>
                {
                    string lookupDirectory;
                    lookupDirectory = options["androidHome"] ?? options["ndkRoot"];

                    string androidApiLevel = RemoveLineTag;
                    if (lookupDirectory != RemoveLineTag)
                    {
                        string latestApiLevel = Util.FindLatestApiLevelInDirectory(Path.Combine(lookupDirectory, "platforms"));
                        if (!string.IsNullOrEmpty(latestApiLevel))
                        {
                            int pos = latestApiLevel.IndexOf("-");
                            if (pos != -1)
                            {
                                androidApiLevel = latestApiLevel.Substring(pos + 1);
                            }
                        }
                    }
                    options["androidMinSdkVersion"] = androidApiLevel;
                    options["AndroidAPILevel"] = RemoveLineTag;
                }),
                Options.Option(Options.Android.General.AndroidAPILevel.Default, () => { options["androidMinSdkVersion"] = RemoveLineTag; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android16, () => { options["androidMinSdkVersion"] = "16"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android17, () => { options["androidMinSdkVersion"] = "17"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android18, () => { options["androidMinSdkVersion"] = "18"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android19, () => { options["androidMinSdkVersion"] = "19"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android20, () => { options["androidMinSdkVersion"] = "20"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android21, () => { options["androidMinSdkVersion"] = "21"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android22, () => { options["androidMinSdkVersion"] = "22"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android23, () => { options["androidMinSdkVersion"] = "23"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android24, () => { options["androidMinSdkVersion"] = "24"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android25, () => { options["androidMinSdkVersion"] = "25"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android26, () => { options["androidMinSdkVersion"] = "26"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android27, () => { options["androidMinSdkVersion"] = "27"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android28, () => { options["androidMinSdkVersion"] = "28"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android29, () => { options["androidMinSdkVersion"] = "29"; }),
                Options.Option(Options.Android.General.AndroidAPILevel.Android30, () => { options["androidMinSdkVersion"] = "30"; })
                );

                context.SelectOptionWithFallback
                (
                () => throw new Error("Android AGDE doesn't support the current Options.Android.General.PlatformToolset"),
                Options.Option(Options.Android.General.PlatformToolset.Default, () => { options["PlatformToolset"] = RemoveLineTag; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.General.UseOfStl.Default, () => { options["UseOfStl"] = RemoveLineTag; }),
                Options.Option(Options.Android.General.UseOfStl.GnuStl_Static, () => { options["UseOfStl"] = "gnustl_static"; }),
                Options.Option(Options.Android.General.UseOfStl.GnuStl_Shared, () => { options["UseOfStl"] = "gnustl_shared"; }),
                Options.Option(Options.Android.General.UseOfStl.LibCpp_Static, () => { options["UseOfStl"] = "cpp_static"; }),
                Options.Option(Options.Android.General.UseOfStl.LibCpp_Shared, () => { options["UseOfStl"] = "cpp_shared"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.General.LinkTimeOptimization.None, () => { options["LinkTimeOptimization"] = "None"; }),
                Options.Option(Options.Android.General.LinkTimeOptimization.LinkTimeOptimization, () => { options["LinkTimeOptimization"] = "LinkTimeOptimization"; }),
                Options.Option(Options.Android.General.LinkTimeOptimization.ThinLinkTimeOptimization, () => { options["LinkTimeOptimization"] = "ThinLinkTimeOptimization"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.General.ClangLinkType.None, () => { options["ClangLinkType"] = RemoveLineTag; }),
                Options.Option(Options.Android.General.ClangLinkType.DeferToNdk, () => { options["ClangLinkType"] = "DeferToNdk"; }),
                Options.Option(Options.Android.General.ClangLinkType.gold, () => { options["ClangLinkType"] = "gold"; }),
                Options.Option(Options.Android.General.ClangLinkType.lld, () => { options["ClangLinkType"] = "lld"; }),
                Options.Option(Options.Android.General.ClangLinkType.bfd, () => { options["ClangLinkType"] = "bfd"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.General.ThumbMode.Default, () => { options["ThumbMode"] = RemoveLineTag; }),
                Options.Option(Options.Android.General.ThumbMode.Thumb, () => { options["ThumbMode"] = "Thumb"; }),
                Options.Option(Options.Android.General.ThumbMode.ARM, () => { options["ThumbMode"] = "ARM"; }),
                Options.Option(Options.Android.General.ThumbMode.Disabled, () => { options["ThumbMode"] = "Disabled"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.General.WarningLevel.TurnOffAllWarnings, () => { options["WarningLevel"] = "TurnOffAllWarnings"; cmdLineOptions["WarningLevel"] = "-w"; }),
                Options.Option(Options.Android.General.WarningLevel.EnableAllWarnings, () => { options["WarningLevel"] = "EnableWarnings"; cmdLineOptions["WarningLevel"] = "-Wall"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.Compiler.CLanguageStandard.Default, () => { options["CLanguageStandard"] = "c11"; cmdLineOptions["CLanguageStandard"] = "-std=c11"; }),
                Options.Option(Options.Android.Compiler.CLanguageStandard.C89, () => { options["CLanguageStandard"] = "c89"; cmdLineOptions["CLanguageStandard"] = "-std=c89"; }),
                Options.Option(Options.Android.Compiler.CLanguageStandard.C99, () => { options["CLanguageStandard"] = "c99"; cmdLineOptions["CLanguageStandard"] = "-std=c99"; }),
                Options.Option(Options.Android.Compiler.CLanguageStandard.C11, () => { options["CLanguageStandard"] = "c11"; cmdLineOptions["CLanguageStandard"] = "-std=c11"; }),
                Options.Option(Options.Android.Compiler.CLanguageStandard.C17, () => { options["CLanguageStandard"] = "c17"; cmdLineOptions["CLanguageStandard"] = "-std=c17"; }),
                Options.Option(Options.Android.Compiler.CLanguageStandard.GNU_C99, () => { options["CLanguageStandard"] = "gnu99"; cmdLineOptions["CLanguageStandard"] = "-std=gnu99"; }),
                Options.Option(Options.Android.Compiler.CLanguageStandard.GNU_C11, () => { options["CLanguageStandard"] = "gnu11"; cmdLineOptions["CLanguageStandard"] = "-std=gnu11"; }),
                Options.Option(Options.Android.Compiler.CLanguageStandard.GNU_C17, () => { options["CLanguageStandard"] = "gnu17"; cmdLineOptions["CLanguageStandard"] = "-std=gnu17"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.Compiler.CppLanguageStandard.Default, () => { options["CppLanguageStandard"] = "cpp11"; cmdLineOptions["CppLanguageStandard"] = "-std=c++11"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.Cpp98, () => { options["CppLanguageStandard"] = "cpp98"; cmdLineOptions["CppLanguageStandard"] = "-std=c++98"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.Cpp11, () => { options["CppLanguageStandard"] = "cpp11"; cmdLineOptions["CppLanguageStandard"] = "-std=c++11"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.Cpp14, () => { options["CppLanguageStandard"] = "cpp14"; cmdLineOptions["CppLanguageStandard"] = "-std=c++14"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.Cpp17, () => { options["CppLanguageStandard"] = "cpp17"; cmdLineOptions["CppLanguageStandard"] = "-std=c++17"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.Cpp1z, () => { options["CppLanguageStandard"] = "cpp1z"; cmdLineOptions["CppLanguageStandard"] = "-std=c++1z"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.GNU_Cpp98, () => { options["CppLanguageStandard"] = "gnupp98"; cmdLineOptions["CppLanguageStandard"] = "-std=gnu++98"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.GNU_Cpp11, () => { options["CppLanguageStandard"] = "gnupp11"; cmdLineOptions["CppLanguageStandard"] = "-std=gnu++11"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.GNU_Cpp14, () => { options["CppLanguageStandard"] = "gnupp14"; cmdLineOptions["CppLanguageStandard"] = "-std=gnu++14"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.GNU_Cpp17, () => { options["CppLanguageStandard"] = "gnupp17"; cmdLineOptions["CppLanguageStandard"] = "-std=gnu++17"; }),
                Options.Option(Options.Android.Compiler.CppLanguageStandard.GNU_Cpp1z, () => { options["CppLanguageStandard"] = "gnupp1z"; cmdLineOptions["CppLanguageStandard"] = "-std=gnu++1z"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.Compiler.DataLevelLinking.Disable, () => { options["EnableDataLevelLinking"] = "false"; cmdLineOptions["EnableDataLevelLinking"] = RemoveLineTag; }),
                Options.Option(Options.Android.Compiler.DataLevelLinking.Enable, () => { options["EnableDataLevelLinking"] = "true"; cmdLineOptions["EnableDataLevelLinking"] = "-fdata-sections"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.Compiler.DebugInformationFormat.None, () => { options["DebugInformationFormat"] = "None"; cmdLineOptions["DebugInformationFormat"] = "-g0"; }),
                Options.Option(Options.Android.Compiler.DebugInformationFormat.FullDebug, () => { options["DebugInformationFormat"] = "FullDebug"; cmdLineOptions["DebugInformationFormat"] = "-g2 -gdwarf-2"; }),
                Options.Option(Options.Android.Compiler.DebugInformationFormat.LineNumber, () => { options["DebugInformationFormat"] = "LineNumber"; cmdLineOptions["DebugInformationFormat"] = "-gline-tables-only"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Android.Compiler.Exceptions.Disable, () => { options["ExceptionHandling"] = "Disabled"; cmdLineOptions["ExceptionHandling"] = "-fno-exceptions"; }),
                Options.Option(Options.Android.Compiler.Exceptions.Enable, () => { options["ExceptionHandling"] = "Enabled"; cmdLineOptions["ExceptionHandling"] = "-fexceptions"; }),
                Options.Option(Options.Android.Compiler.Exceptions.UnwindTables, () => { options["ExceptionHandling"] = "UnwindTables"; cmdLineOptions["ExceptionHandling"] = "-funwind-tables"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.General.TreatWarningsAsErrors.Disable, () => { options["TreatWarningAsError"] = "false"; cmdLineOptions["TreatWarningAsError"] = RemoveLineTag; }),
                Options.Option(Options.Vc.General.TreatWarningsAsErrors.Enable, () => { options["TreatWarningAsError"] = "true"; cmdLineOptions["TreatWarningAsError"] = "-Werror"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.Compiler.BufferSecurityCheck.Enable, () => { options["BufferSecurityCheck"] = "true"; cmdLineOptions["BufferSecurityCheck"] = RemoveLineTag; }),
                Options.Option(Options.Vc.Compiler.BufferSecurityCheck.Disable, () => { options["BufferSecurityCheck"] = "false"; cmdLineOptions["BufferSecurityCheck"] = "-fstack-protector"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.Compiler.FunctionLevelLinking.Disable, () => { options["EnableFunctionLevelLinking"] = "false"; cmdLineOptions["EnableFunctionLevelLinking"] = RemoveLineTag; }),
                Options.Option(Options.Vc.Compiler.FunctionLevelLinking.Enable, () => { options["EnableFunctionLevelLinking"] = "true"; cmdLineOptions["EnableFunctionLevelLinking"] = "-ffunction-sections"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.Compiler.OmitFramePointers.Disable, () => { options["OmitFramePointers"] = "false"; cmdLineOptions["OmitFramePointers"] = "-fno-omit-frame-pointer"; }),
                Options.Option(Options.Vc.Compiler.OmitFramePointers.Enable, () => { options["OmitFramePointers"] = "true"; cmdLineOptions["OmitFramePointers"] = "-fomit-frame-pointer"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.Compiler.Optimization.Disable, () => { options["Optimization"] = "Disabled"; cmdLineOptions["Optimization"] = "-O0"; }),
                Options.Option(Options.Vc.Compiler.Optimization.MinimizeSize, () => { options["Optimization"] = "MinSize"; cmdLineOptions["Optimization"] = "-Os"; }),
                Options.Option(Options.Vc.Compiler.Optimization.MaximizeSpeed, () => { options["Optimization"] = "MaxSpeed"; cmdLineOptions["Optimization"] = "-O2"; }),
                Options.Option(Options.Vc.Compiler.Optimization.FullOptimization, () => { options["Optimization"] = "Full"; cmdLineOptions["Optimization"] = "-O3"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.Compiler.MultiProcessorCompilation.Enable, () => { options["UseMultiToolTask"] = "true"; }),
                Options.Option(Options.Vc.Compiler.MultiProcessorCompilation.Disable, () => { options["UseMultiToolTask"] = "false"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.Compiler.RTTI.Disable, () => { options["RuntimeTypeInfo"] = "false"; cmdLineOptions["RuntimeTypeInfo"] = "-fno-rtti"; }),
                Options.Option(Options.Vc.Compiler.RTTI.Enable, () => { options["RuntimeTypeInfo"] = "true"; cmdLineOptions["RuntimeTypeInfo"] = "-frtti"; })
                );
            }

            public override void SelectPrecompiledHeaderOptions(IGenerationContext context)
            {
                base.SelectPrecompiledHeaderOptions(context);

                var options = context.Options;
                if (options["UsePrecompiledHeader"] == "NotUsing")
                {
                    options["UsePrecompiledHeader"] = FileGeneratorUtilities.RemoveLineTag;
                }
                else
                {
                    context.Options["PrecompiledHeaderThrough"] = Path.GetFileName(context.Options["PrecompiledHeaderThrough"]);
                }
            }

            public override void SelectLinkerOptions(IGenerationContext context)
            {
                var options = context.Options;
                var cmdLineOptions = context.CommandLineOptions;
                var conf = context.Configuration;

                context.SelectOption
                (
                Options.Option(Options.Android.Linker.DebuggerSymbolInformation.IncludeAll, () => { options["DebuggerSymbolInformation"] = "true"; }),
                Options.Option(Options.Android.Linker.DebuggerSymbolInformation.OmitUnneededSymbolInformation, () => { options["DebuggerSymbolInformation"] = "OmitUnneededSymbolInformation"; cmdLineOptions["DebuggerSymbolInformation"] = "-Wl,--strip-unneeded"; }),
                Options.Option(Options.Android.Linker.DebuggerSymbolInformation.OmitDebuggerSymbolInformation, () => { options["DebuggerSymbolInformation"] = "OmitDebuggerSymbolInformation"; cmdLineOptions["DebuggerSymbolInformation"] = "-Wl,--strip-debug"; }),
                Options.Option(Options.Android.Linker.DebuggerSymbolInformation.OmitAllSymbolInformation, () => { options["DebuggerSymbolInformation"] = "OmitAllSymbolInformation"; cmdLineOptions["DebuggerSymbolInformation"] = "-Wl,--strip-all"; })
                );

                context.SelectOption
                (
                Options.Option(Options.Vc.Linker.Incremental.Default, () => { options["IncrementalLink"] = RemoveLineTag; cmdLineOptions["LinkIncremental"] = RemoveLineTag; }),
                Options.Option(Options.Vc.Linker.Incremental.Disable, () => { options["IncrementalLink"] = "false"; cmdLineOptions["LinkIncremental"] = RemoveLineTag; }),
                Options.Option(Options.Vc.Linker.Incremental.Enable, () => { options["IncrementalLink"] = "true"; cmdLineOptions["LinkIncremental"] = "-Wl,--incremental"; })
                );

                context.SelectOption
                (
                    Options.Option(Options.Android.Linker.LibGroup.Enable, () => { options["LibsStartGroup"] = " -Wl,--start-group "; options["LibsEndGroup"] = " -Wl,--end-group "; }),
                    Options.Option(Options.Android.Linker.LibGroup.Disable, () => { options["LibsStartGroup"] = string.Empty; options["LibsEndGroup"] = string.Empty; })
                );
            }

            public override void SelectPlatformAdditionalDependenciesOptions(IGenerationContext context)
            {
                // the static libs must be prefixed with -l: in the additional dependencies field in VS
                // the dynamic libs must not use the -l: prefix, otherwise AGDE fails to look up and copy it (to the package sources)
                var additionalDependencies = context.Options["AdditionalDependencies"].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                context.Options["AdditionalDependencies"] = string.Join(";", additionalDependencies.Select(name =>
                {
                    if (name.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                        return name;
                    return "-l:" + name;
                }
                ));
            }

            public override void SetupPlatformLibraryOptions(ref string platformLibExtension, ref string platformOutputLibExtension, ref string platformPrefixExtension)
            {
                platformLibExtension = ".a";
                platformOutputLibExtension = ".a";
                platformPrefixExtension = string.Empty;
            }

            protected override IEnumerable<string> GetIncludePathsImpl(IGenerationContext context)
            {
                return base.GetIncludePathsImpl(context);
            }

            public override IEnumerable<string> GetLibraryPaths(IGenerationContext context)
            {
                var dirs = new List<string>();
                dirs.Add(@"$(StlLibraryPath)");
                dirs.AddRange(base.GetLibraryPaths(context));

                return dirs;
            }

            public override void SelectPreprocessorDefinitionsVcxproj(IVcxprojGenerationContext context)
            {
                // concat defines, don't add options.Defines since they are automatically added by VS
                var defines = new Strings();
                defines.AddRange(context.Options.ExplicitDefines);
                defines.AddRange(context.Configuration.Defines);

                context.Options["PreprocessorDefinitions"] = defines.JoinStrings(";");
            }

            public override bool HasPrecomp(IGenerationContext context)
            {
                return !string.IsNullOrEmpty(context.Configuration.PrecompHeader);
            }

            private Strings GetSdkIncludePaths(IGenerationContext context)
            {
                return new Strings(@"$(AndroidNdkDirectory)\sources\android");
            }

            #endregion // IPlatformVcxproj implementation
        }
    }
}
