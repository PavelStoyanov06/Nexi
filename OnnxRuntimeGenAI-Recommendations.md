# OnnxRuntimeGenAI API Compatibility Issues and Recommendations

## Current Situation

We've encountered API compatibility issues with the `Microsoft.ML.OnnxRuntimeGenAI` package version 0.6.0. The code we're trying to use includes methods that don't appear to be available in this version:

- `GeneratorParams.SetInputSequences()`
- `Generator.ComputeLogits()`
- `Generator.GenerateNextToken()`

Additionally, there are language version issues with using `ref` and `unsafe` in async methods, which requires C# 13.0 or higher.

## Options for Resolution

### Option 1: Upgrade to a Newer Version

The latest version available is `0.7.0-rc1`. This might include the API methods we're trying to use.

```xml
<PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI" Version="0.7.0-rc1" />
```

Note that this is a release candidate, not a stable release.

### Option 2: Downgrade to an Older Version

Version 0.5.2 is the most widely used version according to the download statistics. It might have a different API that's better documented.

```xml
<PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI" Version="0.5.2" />
```

### Option 3: Adapt to the Current API

We need to find the correct API for version 0.6.0. This might involve:

1. Checking the official documentation at https://onnxruntime.ai/docs/genai/
2. Looking at examples in the GitHub repository: https://github.com/microsoft/onnxruntime-genai
3. Examining the sample code provided in the NuGet package description

### Option 4: Use a Different Approach

If the OnnxRuntimeGenAI API is unstable or poorly documented, consider:

1. Using the standard OnnxRuntime API directly
2. Using a different library for running LLMs
3. Using a higher-level abstraction like Microsoft.SemanticKernel.Connectors.Onnx

## Next Steps

1. **Research the correct API**: Look for examples specifically for version 0.6.0
2. **Test with a simple example**: Create a minimal project to test the API
3. **Consider version changes**: Evaluate if upgrading or downgrading is the best option
4. **Update the language version**: Add `<LangVersion>13.0</LangVersion>` to the project file

## Resources

- [OnnxRuntime GenAI Documentation](https://onnxruntime.ai/docs/genai/)
- [GitHub Repository](https://github.com/microsoft/onnxruntime-genai)
- [NuGet Package](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntimeGenAI/)
- [C# API Reference](https://onnxruntime.ai/docs/genai/api/csharp.html) 