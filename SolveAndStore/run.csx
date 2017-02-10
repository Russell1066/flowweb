#r "..\bin\FlowCore.dll"
#load "..\Common\common.csx"

using System;
using System.Net;
using System.Diagnostics;
using System.Threading;

using SolverCore;

public class BoardTable
{
    public string PartitionKey { get { return Board.BoardSize.ToString(); } }
    public string RowKey { get { return TraceId; } }
    public string TraceId { get; set; }
    public string Name { get; set; }
    public FlowBoard.BoardDefinition Board { get; set; }
};

public static void Run(string myQueueItem, ICollector<BoardTable> outputTable,
    CloudTable traceIds, 
    TraceWriter log)
{
    var logger = new Logger(log, myQueueItem);

    logger.Info($"Queue trigger function processing : {myQueueItem} (should be redundant)");
    var retrieve = traceIds.Execute(TableOperation.Retrieve<UploadResults>("0", traceId));  // BUGBUG - needs exception handling
    var results = retrieve.Result as UploadResults;
    var boardDescription = results.Board;

    // Duplicate processing - we're already done
    if(results.Accepted.HasValue)
    {
        return;
    }

    // Assume false, only update to true if everything works
    results.Accepted = false;

    // It can't be in the table if it isn't already valid
    if (!boardDescription.IsValid())
    {
        results.Results = $"board failures reports {wrapper.Board.DescribeFailures()}";
        traceIds.Execute(TableOperation.Merge(results));
        logger.Error(results.Results);
        return;
    }

    CancellationTokenSource TokenSource = new CancellationTokenSource();
    var board = new FlowBoard();
    board.InitializeBoard(wrapper.Board);
    Stopwatch s = new Stopwatch();
    s.Start();
    try
    {
        TokenSource.CancelAfter(2 * 60 * 1000);
        logger.Info($"Solver starting");
        var task = Solver.Solve(board, TokenSource.Token);
        task.Wait();
        logger.Info($"Solver completed {task.Result}");
    }
    catch (AggregateException agg) when (agg.InnerException is OperationCanceledException)
    {
        logger.Error(agg.InnerException.ToString());
        logger.Info($"TraceId : {wrapper.TraceId} failed");
        results.Results = $"Solving took too long";
        traceIds.Execute(TableOperation.Merge(results));
        logger.Info($"TraceId updated with {results.Results}");
        return;
    }
    finally
    {
        s.Stop();
        logger.Info($"took : {s.Elapsed}");
    }

    var output = new BoardTable()
    {
        TraceId = wrapper.TraceId,
        Name = wrapper.Name,
        Board = wrapper.Board,
    };

    try
    {
        outputTable.Add(output);
        logger.Info($"TraceId : {output.TraceId} succeeded");
        results.Accepted = true;
        results.Results = "Accepted";
        traceIds.Execute(TableOperation.Merge(results));
    }
    catch
    {
        // BUGBUG - check to see if this has already been processed
        logger.Info($"TraceId : {output.TraceId} failed");
        traceIds.Execute(TableOperation.Merge(results));
    }
}