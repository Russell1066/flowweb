#r "Newtonsoft.Json"
#r "..\bin\FlowCore.dll"

using System;
using System.Net;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;

using SolverCore;

public class BoardWrapper
{
    public string TraceId { get; set; }
    public string Name { get; set; }
    public FlowBoard.BoardDefinition Board { get; set; }

    public BoardWrapper()
    {
        TraceId = Guid.NewGuid().ToString();
    }
};

