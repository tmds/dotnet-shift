interface IProjectReader
{
    public bool TryReadProjectInfo(string path, [NotNullWhen(true)]out ProjectInformation? projectInformation, out List<string> validationErrors);
}