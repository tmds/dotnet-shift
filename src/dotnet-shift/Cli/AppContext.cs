using System.CommandLine;

namespace Cli;

class AppContext
{
    public AppContext(ParseResult result, AppServices services)
    {
        ParseResult = result;
        Services = services;
    }

    public ParseResult ParseResult { get; }

    public AppServices Services { get; }

    // The following properties hold results of handler steps.
    public LoginContext? LoginContext { get; set; }
}