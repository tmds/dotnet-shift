record class ContainerResources
{
    public ResourceQuantity? ContainerCpuRequest { get; init; }
    public ResourceQuantity? ContainerCpuLimit { get; init; }
    public ResourceQuantity? ContainerMemoryRequest { get; init; }
    public ResourceQuantity? ContainerMemoryLimit { get; init; }
}