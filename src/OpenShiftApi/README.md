This class library contains an API client generated using [NSwag](https://github.com/RicoSuter/NSwag) based on the OpenShift swagger spec.

The `openshift-api.json` file was obtained using these commands:

```
oc login ...
oc get --raw /openapi/v2 >openshift-api.json
```

We generate `CLIENT_CS` using `SWAGGER_JSON`.
```
SWAGGER_JSON=$(realpath openshift-api.json)
CLIENT_CS=$(realpath ./OpenShiftApiClient.cs)
```

The OpenShift spec has duplicate paarameter names, and NSWag has an issue with these: https://github.com/RicoSuter/NSwag/issues/3442.

We'll patch NSwag for this:
```
cd /tmp
git clone https://github.com/RicoSuter/NSwag
git checkout v13.18.0
cd NSWag
```

Patch:
```
diff --git a/src/NSwag.CodeGeneration.CSharp/Models/CSharpOperationModel.cs b/src/NSwag.CodeGeneration.CSharp/Models/CSharpOperationModel.cs
index 0c11abc0b..dbe2bc15c 100644
--- a/src/NSwag.CodeGeneration.CSharp/Models/CSharpOperationModel.cs
+++ b/src/NSwag.CodeGeneration.CSharp/Models/CSharpOperationModel.cs
@@ -75,7 +75,7 @@ public class CSharpOperationModel : OperationModelBase<CSharpParameterModel, CSh
                 .Select(parameter =>
                     new CSharpParameterModel(
                         parameter.Name,
-                        GetParameterVariableName(parameter, _operation.Parameters),
+                        GetParameterVariableName(parameter, _operation.ActualParameters),
                         GetParameterVariableIdentifier(parameter, _operation.Parameters),
                         ResolveParameterType(parameter), parameter, parameters,
                         _settings.CodeGeneratorSettings,

```

Now we can generate the client.
```
cs src/NSwag.ConsoleCore
dotnet run -f net7.0 -- openapi2csclient /Input:$SWAGGER_JSON /Namespace:OpenShift /Output:$CLIENT_CS /ClassName:OpenShiftClient /GenerateOptionalParameters:true /ArrayType:System.Collections.Generic.List /ArrayInstanceType:System.Collections.Generic.List /ArrayBaseType:System.Collections.Generic.List/DictionaryType:System.Collections.Generic.Dictionary /DictionaryInstanceType:System.Collections.Generic.Dictionary /DictionaryBaseType:System.Collections.Generic.Dictionary /GenerateDefaultValues:false /GenerateOptionalPropertiesAsNullable:true  
```

The generated client needs to be patched for some duplicate enum value names: replace `__ = 3` by `___ = 3` in the generated file.
