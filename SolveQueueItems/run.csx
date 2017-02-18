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

public class SolverTable : TableEntity
{
    public string TraceId { get; set; }
    public int SolutionId { get; set; }
    public int SolutionCount { get; set; }
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public bool Completed { get; set; }
    public bool IsSolution { get; set; }

    public SolverTable() { }

    public SolverTable(string traceId, int solutionId)
    {
        PartitionKey = traceId;
        RowKey = solutionId.ToString();
        TraceId = traceId;
        SolutionId = solutionId;
    }
}

public static async void Run(string myQueueItem, CloudTable solverTable,
    CloudTable traceIds,
    CancellationToken token,
    TraceWriter log)
{
    var logger = new Logger(log, myQueueItem);
    var traceId = myQueueItem;

    logger.Info($"Queue trigger function processing : {myQueueItem} (should be redundant)");
    var retrieve = traceIds.Execute(TableOperation.Retrieve<UploadResults>("0", traceId));  // BUGBUG - needs exception handling
    var results = retrieve.Result as UploadResults;
    logger.Info($"Testing if we are running new code");

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
    var solver = SolverMgr.GetSolver(myQueueItem, new FlowBoard());
    SolverTable solverTableItem = new SolverTable(traceId, Int32.Parse(solver.Wrapper.SolutionId))
    {
        SolutionCount = Int32.Parse(solver.Wrapper.SolutionSetId),
        StartTime = DateTime.Now.ToString("o"),
    };

    logger.Info($"Solving {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : Start");
    solverTableItem.IsSolution = await solver.Solver(token);
    logger.Info($"Solving {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : End");
    solverTableItem.EndTime = DateTime.Now.ToString("o");
    solverTableItem.Completed = true;

    await solverTable.ExecuteAsync(TableOperation.Insert(solverTableItem));
}