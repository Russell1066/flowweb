#r "..\bin\FlowCore.dll"
#r "Microsoft.WindowsAzure.Storage"

using System;
using System.Net;
using System.Diagnostics;
using System.Threading;
using Microsoft.WindowsAzure.Storage.Table;

using SolverCore;

public static string EventTraceId;

public class Logger
{
    public dynamic Log { get; }

    public Logger(dynamic log, string eventTraceId = null)
    {
        Log = log;

        if (eventTraceId != null)
        {
            EventTraceId = eventTraceId;
        }
    }

    public void Info(string log)
    {
        Log?.Info($"{EventTraceId} : {log}");
    }

    public void Error(string log)
    {
        Log?.Error($"{EventTraceId} : {log}");
    }
}

public class BadRequest
{
    public string TraceId { get; set; }
    public string FailureType { get; set; }
    public string Description { get; set; }

    public BadRequest()
    {
        TraceId = EventTraceId;
    }
}

public class UploadResults : TableEntity
{
    public const string PARTITIONKEY = "0";
    public string TraceId { get; set; }
    public string Name { get; set; }
    public FlowBoard.BoardDefinition Board { get; set; }
    public bool? Accepted { get; set; }
    public string Results { get; set; }

    public UploadResults()
    {
        PartitionKey = PARTITIONKEY;
        RowKey = Guid.NewGuid().ToString();
        TraceId = RowKey;
    }
}

