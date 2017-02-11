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

public static void Run(string myQueueItem, ICollector<BoardTable> outputTable,
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
        logger.Error("queue Item processed a second time");
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

    CancellationTokenSource TokenSource = new CancellationTokenSource();
    var board = new FlowBoard();
    board.InitializeBoard(boardDescription);
    Stopwatch s = new Stopwatch();
    s.Start();
    try
    {
        TokenSource.CancelAfter(2 * 60 * 1000);
        logger.Info($"Solver starting");
        var task = Solver.Solve(board, TokenSource.Token);
        var sleepLogger = Task.Run(() =>
        {
            for (;;)
            {
                Task.Delay(3 * 60 * 1000, TokenSource.Token).Wait();
                logger.Info("...Solving is hard...");
            }
        });
        Task.WaitAny(task, sleepLogger);
        logger.Info($"Solver completed {task.Result}");
    }
    catch (AggregateException agg) when (agg.InnerException is OperationCanceledException)
    {
        logger.Error(agg.InnerException.ToString());
        logger.Info($"failed");
        results.Results = $"Solving took too long";
        results.ProcessEndTime = DateTime.Now.ToString("o");
        traceIds.Execute(TableOperation.Merge(results));
        logger.Info($"TraceId table updated with {results.Results}");
        return;
    }
    finally
    {
        s.Stop();
        logger.Info($"took : {s.Elapsed}");
    }

    var output = new BoardTable()
    {
        TraceId = traceId,
        Name = results.Name,
        Board = boardDescription,
    };

    try
    {
        outputTable.Add(output);
        logger.Info($"Accepted");
        results.Accepted = true;
        results.Results = "Accepted";
        results.ProcessEndTime = DateTime.Now.ToString("o");
        traceIds.Execute(TableOperation.Merge(results));
    }
    catch
    {
        // BUGBUG - check to see if this has already been processed
        logger.Error($"failed");
        traceIds.Execute(TableOperation.Merge(results));
    }
}