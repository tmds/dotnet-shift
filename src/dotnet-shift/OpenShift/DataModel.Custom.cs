namespace OpenShift;

#nullable disable

sealed record DeploymentTrigger
{
    [Newtonsoft.Json.JsonProperty("from")]
    public DeploymentTriggerFrom From { get; set; }

    [Newtonsoft.Json.JsonProperty("fieldPath")]
    public string FieldPath { get; set; }

    [Newtonsoft.Json.JsonProperty("paused")]
    public string Paused { get; set; }

    public class DeploymentTriggerFrom
    {
        [Newtonsoft.Json.JsonProperty("kind")]
        public string Kind { get; set; }

        [Newtonsoft.Json.JsonProperty("name")]
        public string Name { get; set; }
    }
}