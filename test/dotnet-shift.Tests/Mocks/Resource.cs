using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json;

// Helper class to work with the opaque resource objects held by the MockOpenShiftServer.
static class Resource
{
    public static T Clone<T>(T value)
    {
        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
    }

    public static object Clone(object value)
    {
        return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value), value.GetType())!;
    }

    public static IDictionary<string, string>? GetLabels(object resource)
        => (IDictionary<string, string>?)GetPropValue(GetMetadata(resource), "Labels");

    public static string? GetResourceVersion(object resource)
        => (string?)GetPropValue(GetMetadata(resource), "ResourceVersion");

    public static string? GetName(object resource)
        => (string?)GetPropValue(GetMetadata(resource), "Name");

    private static object? GetMetadata(object resource)
        => GetPropValue(resource, "Metadata");

    private static object? GetPropValue(object? o, string propName)
    {
        if (o is null)
        {
            return null;
        }
        Type type = o.GetType();
        PropertyInfo? prop = type.GetProperty(propName);
        Assert.NotNull(prop);

        return prop.GetValue(o);
    }

    public static void SetResourceVersion(object resource, string? resourceVersion)
    {
        Type type = resource.GetType();
        PropertyInfo metadataProp = type.GetProperty("Metadata")!;
        PropertyInfo resourceVersionProp = metadataProp.PropertyType.GetProperty("ResourceVersion")!;

        object? metadata = metadataProp.GetValue(resource);
        if (metadata == null)
        {
            metadata = Activator.CreateInstance(metadataProp.PropertyType);
            metadataProp.SetValue(resource, metadata);
        }
        resourceVersionProp.SetValue(metadata, resourceVersion);
    }

    public static void SetStatus(object resource, object? status)
    {
        Type type = resource.GetType();
        PropertyInfo statusProp = type.GetProperty("Status")!;
        if (statusProp is not null)
        {
            statusProp.SetValue(resource, null);
        }
    }

    public static void StrategicMergeObjectWith(object current, object mergeWith)
    {
        Debug.Assert(current.GetType() == mergeWith.GetType());

        const BindingFlags bindingFlags =
                BindingFlags.Instance |
                BindingFlags.Public;

        foreach (PropertyInfo propertyInfo in current.GetType().GetProperties(bindingFlags))
        {
            var attribute = propertyInfo.GetCustomAttribute<OpenShift.PatchMergeKeyAttribute>();
            propertyInfo.SetValue(current, GetStrategicMergedValue(propertyInfo.GetValue(current), propertyInfo.GetValue(mergeWith), attribute?.MergeKey));
        }
    }

    private static object? GetStrategicMergedValue(object? current, object? with, string? mergeKey)
    {
        if (with is null)
        {
            return current;
        }
        if (current is null)
        {
            return with;
        }

        Debug.Assert(current.GetType() == with.GetType());
        Type type = current.GetType();
        if (type == typeof(string))
        {
            return with;
        }
        // class from the OpenShift datamodel.
        if (type.FullName?.StartsWith("OpenShift.") == true)
        {
            StrategicMergeObjectWith(current, with);
            return current;
        }
        // value type
        if (type.IsValueType && type.FullName?.StartsWith("System.") == true)
        {
            return with;
        }
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            StrategicMergeDictionaryWith((System.Collections.IDictionary)current, (System.Collections.IDictionary)with);
            return current;
        }
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(List<>))
        {
            StrategicMergeListWith((System.Collections.IList)current, (System.Collections.IList)with, mergeKey);
            return current;
        }

        throw new NotImplementedException($"Unhandled type: {type.FullName}");
    }

    private static void StrategicMergeDictionaryWith(System.Collections.IDictionary current, System.Collections.IDictionary mergeWith)
    {
        foreach (System.Collections.DictionaryEntry mergeValue in mergeWith)
        {
            if (current.Contains(mergeValue.Key))
            {
                current[mergeValue.Key] = GetStrategicMergedValue(current[mergeValue.Key], mergeValue.Value, null);
            }
            else
            {
                current[mergeValue.Key] = mergeValue.Value;
            }
        }
    }

    private static void StrategicMergeListWith(System.Collections.IList current, System.Collections.IList mergeWith, string? mergeKey)
    {
        if (mergeKey is null)
        {
            foreach (var mergeValue in mergeWith)
            {
                current.Add(mergeValue);
            }
        }
        else
        {
            PropertyInfo? mergeKeyProperty = null;

            foreach (var mergeValue in mergeWith)
            {
                mergeKeyProperty ??= mergeValue.GetType().GetProperty(mergeKey);

                bool found = false;

                object mergeKeyValue = mergeKeyProperty!.GetValue(mergeValue)!;

                foreach (var item in current)
                {
                    if (mergeKeyProperty.GetValue(item) == mergeKeyValue)
                    {
                        StrategicMergeObjectWith(item, mergeValue);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    current.Add(mergeValue);
                }
            }
        }
    }
}