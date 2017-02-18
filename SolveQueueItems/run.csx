#r "Newtonsoft.Json"
#r "..\bin\FlowCore.dll"
#r "Microsoft.WindowsAzure.Storage"
#load "..\Common\common.csx"

using System;
using System.Net;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage.Table;

using SolverCore;

class SolverTable : TableEntity
{
    public string TraceId { get; set; }
    public string SolutionId { get; set; }
    public string SolutionCount { get; set; }
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public bool Completed { get; set; }

    public SolverTable() { }

    public SolverTable(string traceId, string solutionId)
    {
        PartitionKey = traceId;
        RowKey = solutionId;
        TraceId = traceId;
        SolutionId = solutionId;
    }
}

public static void Run(string myQueueItem, ICollector<SolverTable> solverTable,
    CloudTable traceIds,
    TraceWriter log)
{
    var logger = new Logger(log, myQueueItem);
    var traceId = myQueueItem;

    logger.Info($"Queue trigger function processing : {myQueueItem} (should be redundant)");
    var retrieve = traceIds.Execute(TableOperation.Retrieve<UploadResults>("0", traceId));  // BUGBUG - needs exception handling
    var results = retrieve.Result as UploadResults;

    if (results == null)
    {
        logger.Error("queue Item with no table item - should be impossible");
        return;
    }

    // Duplicate processing - we're already done
    if (results.Processed)
    {
        logger.Error("traceId with queue items after it is marked processed");
        return;
    }

    results.ProcessStartTime = DateTime.Now.ToString("o");
    traceIds.Execute(TableOperation.Merge(results));

    var boardDescription = JsonConvert.DeserializeObject<FlowBoard.BoardDefinition>(results.Board);

    results.Processed = true;

    // It can't be in the table if it isn't already valid
    if (!boardDescription.IsValid())
    {
        results.Results = $"board failures reports {boardDescription.DescribeFailures()}";
        results.ProcessEndTime = DateTime.Now.ToString("o");
        traceIds.Execute(TableOperation.Merge(results));
        logger.Error(results.Results);
        return;
    }

    // Solve this instance
    var wrapper = SolverMgr.GetSolverWrapper(solverData, tracingId.ToString());
    var solver = SolverMgr.GetSolver(wrapper, Game);
    // Temporary Hack:
    var solver2Data = JsonConvert.DeserializeObject<Solver2.SolverData>(wrapper.SolutionData);
    SolverTable solverTableItem = new SolverTable(traceId, solver2Data.SolutionIndex)
    {
        SolutionCount = solver2Data.SolverCount,
        StartTime = DateTime.Now.ToString("o"),
    };

    logger.Info($"Solving item");

    if (await solver(TokenSource.Token))
    {
        break;
    }
    solverTableItem.EndTime = DateTime.Now.ToString("o");
    solverTableItem.Completed = true;

    solverTable.Execute(TableOperation.Add(solverTableItem));
}