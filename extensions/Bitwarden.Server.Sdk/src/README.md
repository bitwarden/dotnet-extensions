# Bitwarden.Server.Sdk

The Bitwarden Server SDK is used as a [MSBuild SDK][msbuild-sdk]. This allows us to have a lot of
customizability of the applications that consume us. It allows for us to have features turned on or
off using compile time switches. This means consumers can only take the features they want and can
have a smaller output instead of being forced to take on all features.

## Structure

The [./Sdk/Sdk.props](./Sdk/Sdk.props) file is expected to be imported first and done through the
`<Sdk>` element. Therefore no properties set in a consumers csproj will have been read yet. For this
reason the consumer can't make decisions about what features they want in the SDK and have those
decisions be known to us in the props file. This is why all we do is declare a marker property
`UsingBitwardenServerSdk` to `true`.

The [./Sdk/Sdk.targets](./Sdk/Sdk.targets) file is expected to be imported last. This way,
properties defined in a consumer project will have been evaluated and can be used to make decisions
on which packages to automatically reference.

The [`Content`](./Content/) directory are additional files that are packaged into the resulting
NuGet package in their plaintext form. The `Sdk.targets` file can use files from here and include
them in the consuming projects compilation using `<Compile Include="$(SdkContentRoot)/File.cs" />`.
Since the files in Content aren't automatically compiled when building the SDK project it's
important for an integration test to consume the SDK and that file to check that it will be able to
build.

[msbuild-sdk]:[https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk?view=vs-2022]
