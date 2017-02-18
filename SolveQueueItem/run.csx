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

public class SolverTableEntry : TableEntity
{
    public string TraceId { get; set; }
    public int Retries { get; set; }
    public int SolutionId { get; set; }
    public int SolutionCount { get; set; }
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public bool Completed { get; set; }
    public bool IsSolution { get; set; }
    public bool Skipped { get; set; }

    public SolverTableEntry() { }

    public SolverTableEntry(string traceId, int solutionId)
    {
        PartitionKey = traceId;
        RowKey = solutionId.ToString();
        TraceId = traceId;
        SolutionId = solutionId;
    }
}

private const int READSOLUTIONDELAY = 5 * 1000; // miliseconds

private static Logger logger;

public static void Run(string myQueueItem,
    CloudTable solverTable,
    CloudTable outputTable,
    CloudTable traceIds,
    CancellationToken token,
    TraceWriter log)
{
    log.Info(myQueueItem);

    var board = new FlowBoard(); // Keep this separate, we might want to write it to a solutions table
    SolverMgr.SolutionRunner solver = SolverMgr.GetSolver(myQueueItem, board);
    logger = new Logger(log, solver.Wrapper.TracingId);
    string traceId = solver.Wrapper.TracingId;

    // If this has already been done - don't do it again
    SolverTableEntry solverTableEntry = GetSolverTableEntry(solverTable, solver.Wrapper);
    if (solverTableEntry?.Completed)
    {
        logger.Info($"Already run to completion {solverTableEntry.SolutionId} solution was {solverTableEntry.IsSolution}");
        return;
    }

    if (solverTableEntry == null)
    {
        solverTableEntry = new SolverTableEntry(traceId, Int32.Parse(solver.Wrapper.SolutionId))
        {
            SolutionCount = Int32.Parse(solver.Wrapper.SolutionSetId),
            StartTime = DateTime.Now.ToString("o"),
        };
    }
    else
    {
        solverTableEntry.Retries++;
    }

    // If the overall solution has already been done
    // then mark this as skipped and be on our way
    UploadResults uploadResults = GetUploadResultEntry(traceIds, traceId);
    if (uploadResults == null)
    {
        logger.Error("queue Item with no table item - should be impossible");
        WriteQueueItemFailure(solverTable, solverTableEntry);
        return;
    }

    // Duplicate processing - we're already done
    if (uploadResults.Processed)
    {
        logger.Error("queue Item processed a second time");
        WriteQueueItemFailure(solverTable, solverTableEntry);
        return;
    }

    FlowBoard.BoardDefinition boardDescription = JsonConvert.DeserializeObject<FlowBoard.BoardDefinition>(uploadResults.Board);

    logger.Info($"Solving {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : Start");

    // Create a token source to exit the task if someone else solves the problem
    var tokenSource = new CancellationTokenSource();
    token.Register(() => tokenSource.Cancel());

    var task = solver.Solver(tokenSource.Token);
    bool done = false;
    var sleepLogger = Task.Run(() =>
    {
        while (!done)
        {
            Task.Delay(READSOLUTIONDELAY, TokenSource.Token).Wait();
            if (GetUploadResultEntry(traceIds, traceId).Processed)
            {
                tokenSource.Cancel();
                return;
            }

            if (!done)
            {
                logger.Info("...Solving is hard...");
            }
        }
    }, tokenSource.Token);

    try
    {
        Task.WaitAny(task, sleepLogger);
        done = true;
        solverTableEntry.IsSolution = task.Result;
        logger.Info($"IsSolution : {solverTableEntry.IsSolution}");
    }
    catch (AggregateException agg) when (agg.InnerException is OperationCanceledException)
    {
        solverTableEntry.Completed = false;
        WriteQueueItemFailure(solverTable, solverTableEntry, false);
        return;
    }
    catch (Exception ex)
    {
        logger.Error($"unexpected - work in progress {ex.ToString()}");
    }

    solverTableEntry.EndTime = DateTime.Now.ToString("o");
    solverTableEntry.Completed = true;
    logger.Info("");
    logger.Info($"Writing {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : {solverTableEntry.IsSolution}");
    logger.Info($"PartitionKey = {solverTableEntry.PartitionKey}");
    logger.Info($"RowKey = {solverTableEntry.RowKey}");
    logger.Info($"TraceId = {solverTableEntry.TraceId}");
    logger.Info($"SolutionId = {solverTableEntry.SolutionId}");
    logger.Info($"SolutionCount = {solverTableEntry.SolutionId}");
    logger.Info($"StartTime = {solverTableEntry.StartTime}");
    logger.Info($"EndTime = {solverTableEntry.EndTime}");
    logger.Info($"Completed = {solverTableEntry.Completed}");
    logger.Info($"IsSolution = {solverTableEntry.IsSolution}");
    solverTable.ExecuteAsync(TableOperation.InsertOrMerge(solverTableEntry)).Wait();
    logger.Info($"Wrote {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : {solverTableEntry.IsSolution}");

    // Finally, update to say we found it
    if (solverTableEntry.IsSolution)
    {
        WriteSuccess(outputTable, traceIds, traceId, uploadResults, boardDescription);
    }
}

void WriteSuccess(CloudTable outputTable, CloudTable traceIds, string traceId, UploadResults uploadResults, FlowBoard.BoardDefinition boardDescription)
{
    logger.Info($"Writing success");
    var output = new BoardTable(traceId, results.Name, boardDescription);

    outputTable.Execute(TableOperation.Insert(output));

    logger.Info($"Accepted");
    results.Accepted = true;
    results.Results = "Accepted";
    results.ProcessEndTime = DateTime.Now.ToString("o");
    traceIds.Execute(TableOperation.Merge(results));
}

private static SolverTableEntry GetSolverTableEntry(CloudTable solverTable, SolverWrapper wrapper)
{
    var solverTableResult = solverTable.Execute(TableOperation.Retrieve<SolverTableEntry>(wrapper.TracingId, wrapper.SolutionId));
    return solverTableResult.Result as SolverTableEntry;
}

private static UploadResults GetUploadResultEntry(CloudTable traceIds, string traceId)
{
    var traceIdResult = traceIds.Execute(TableOperation.Retrieve<UploadResults>(UploadResults.PARTITIONKEY, traceId));
    return traceIdResult.Result as UploadResults;
}

private static void WriteQueueItemFailure(CloudTable solverTable, SolverTableEntry solverTableEntry, bool skipped = true)
{
    solverTableEntry.IsSolution = false;
    solverTableEntry.Skipped = skipped;
    solverTableEntry.EndTime = DateTime.Now.ToString("o");
    solverTable.ExecuteAsync(TableOperation.InsertOrMerge(solverTableEntry)).Wait();
    string failedOrSkipped = skipped ? "skipped" : "failed";
    logger.Info($"QueueItem {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : {solverTableEntry.IsSolution} {failedOrSkipped}");
}