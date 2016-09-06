#r @"src/packages/FAKE/tools/FakeLib.dll"
open System.IO
open Fake
open Fake.AssemblyInfoFile
open Fake.Git.Information
open Fake.SemVerHelper

let buildOutputPath = "./build_output"
let buildArtifactPath = "./build_artifacts"
let nugetWorkingPath = FullName "./build_temp"
let packagesPath = FullName "./src/packages"
let keyFile = FullName "./NewId.snk"

let assemblyVersion = "3.0.0.0"
let baseVersion = "3.0.0"

let semVersion : SemVerInfo = parse baseVersion

let Version = semVersion.ToString()

let branch = (fun _ ->
  (environVarOrDefault "APPVEYOR_REPO_BRANCH" (getBranchName "."))
)

let FileVersion = (environVarOrDefault "APPVEYOR_BUILD_VERSION" (Version + "." + "0"))

let informationalVersion = (fun _ ->
  let branchName = (branch ".")
  let label = if branchName="master" then "" else " (" + branchName + "/" + (getCurrentSHA1 ".").[0..7] + ")"
  (FileVersion + label)
)

let nugetVersion = (fun _ ->
  let branchName = (branch ".")
  let label = if branchName="master" then "" else "-" + (branchName)
  (Version + label)
)

let InfoVersion = informationalVersion()
let NuGetVersion = nugetVersion()


printfn "Using version: %s" Version

Target "Clean" (fun _ ->
  ensureDirectory buildOutputPath
  ensureDirectory buildArtifactPath
  ensureDirectory nugetWorkingPath

  CleanDir buildOutputPath
  CleanDir buildArtifactPath
  CleanDir nugetWorkingPath
)

Target "RestorePackages" (fun _ -> 
     "./src/NewId.sln"
     |> RestoreMSSolutionPackages (fun p ->
         { p with
             OutputPath = packagesPath
             Retries = 4 })
)

Target "Build" (fun _ ->

  CreateCSharpAssemblyInfo @".\src\SolutionVersion.cs"
    [ Attribute.Title "NewId"
      Attribute.Description "NewId is an ordered 128-bit unique identifier generator using the Flake algorithm."
      Attribute.Product "NewId"
      Attribute.Version assemblyVersion
      Attribute.FileVersion FileVersion
      Attribute.InformationalVersion InfoVersion
    ]

  let buildMode = getBuildParamOrDefault "buildMode" "Release"
  let setParams defaults = { 
    defaults with
        Verbosity = Some(Quiet)
        Targets = ["Clean"; "Build"]
        Properties =
            [
                "Optimize", "True"
                "DebugSymbols", "True"
                "RestorePackages", "True"
                "Configuration", buildMode
                "SignAssembly", "True"
                "AssemblyOriginatorKeyFile", keyFile
                "TargetFrameworkVersion", "v4.5.2"
                "Platform", "Any CPU"
            ]
  }

  build setParams @".\src\NewId.sln"
      |> DoNothing
)

let testDlls = !! ("./src/NewId.Tests/bin/Release/*.Tests.dll")

Target "UnitTests" (fun _ ->
    testDlls
        |> NUnit (fun p -> 
            {p with
                Framework = "v4.0.30319"
                DisableShadowCopy = true; 
                OutputFile = buildArtifactPath + "/nunit-test-results.xml"})
)

type packageInfo = {
    Project: string
    PackageFile: string
    Summary: string
    Files: list<string*string option*string option>
}

Target "Package" (fun _ ->

  let nugs = [| { Project = "NewId"
                  Summary = "NewId is an ordered 128-bit unique identifier generator using the Flake algorithm."
                  PackageFile = @".\src\NewId\packages.config"
                  Files = [ (@"..\src\NewId\bin\Release\NewId.*", Some @"lib\net452", None);
                            (@"..\src\NewId\**\*.cs", Some "src", None) ] }
             |]

  nugs
    |> Array.iter (fun nug ->

      let getDeps daNug : NugetDependencies =
        if daNug.Project = "NewId" then []
        else ("NewId", NuGetVersion) :: (getDependencies daNug.PackageFile)

      let setParams defaults = {
        defaults with 
          Authors = ["Chris Patterson"]
          Description = "NewId is an ordered 128-bit unique identifier generator using the Flake algorithm."
          OutputPath = buildArtifactPath
          Project = nug.Project
          Dependencies = (getDeps nug)
          Summary = nug.Summary
          SymbolPackage = NugetSymbolPackage.Nuspec
          Version = NuGetVersion
          WorkingDir = nugetWorkingPath
          Files = nug.Files
      } 

      NuGet setParams (FullName "./template.nuspec")
    )
)

Target "Default" (fun _ ->
  trace "Build starting..."
)

"Clean"
  ==> "RestorePackages"
  ==> "Build"
  ==> "UnitTests"
  ==> "Package"
  ==> "Default"

RunTargetOrDefault "Default"