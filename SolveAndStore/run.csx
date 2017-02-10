#r "Newtonsoft.Json"
#r "..\bin\FlowCore.dll"
#load "..\Common\common.csx"

using System;
using System.Net;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;

using SolverCore;

public class BoardTable
{
    public string PartitionKey { get { return Board.BoardSize.ToString(); } }
    public string RowKey { get { return TraceId; } }
    public string TraceId { get; set; }
    public string Name { get; set; }
    public FlowBoard.BoardDefinition Board { get; set; }
};

public class UploadResults
{
    public string PartitionKey => 0.ToString();
    public string RowKey { get { return TraceId; } }
    public string TraceId { get; set; }
    public bool Accepted { get; set; }
    public string Results { get; set; }
}

public static void Run(string myQueueItem, ICollector<BoardTable> outputTable, ICollector<UploadResults> traceIdTable, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed: {myQueueItem}");

    var wrapper = JsonConvert.DeserializeObject<BoardWrapper>(myQueueItem);
    var results = new UploadResults()
    {
        TraceId = wrapper.TraceId,
        Accepted = false,
    };

    // It can't be in the queue if it isn't alread valid!
    if (!wrapper.Board.IsValid())
    {
        results.Results = $"board failures reports {wrapper.Board.DescribeFailures()}";
        traceIdTable.Add(results);
        log.Info(results.Results);
    }

    CancellationTokenSource TokenSource = new CancellationTokenSource();
    var board = new FlowBoard();
    board.InitializeBoard(wrapper.Board);
    Stopwatch s = new Stopwatch();
    s.Start();
    try
    {
        TokenSource.CancelAfter(2 * 60 * 1000);
        log.Info($"Solver starting");
        var task = Solver.Solve(board, TokenSource.Token);
        task.Wait();
        log.Info($"Solver completed {task.Result}");
    }
    catch (AggregateException agg) when (agg.InnerException is OperationCanceledException)
    {
        log.Error(agg.InnerException.ToString());
        log.Info($"TraceId : {wrapper.TraceId} failed");
        results.Results = $"Solving took too long";
        traceIdTable.Add(results);
        log.Info($"TraceId updated with {results.Results}");
        return;
    }
    finally
    {
        s.Stop();
        log.Info($"took : {s.Elapsed}");
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
        log.Info($"TraceId : {output.TraceId} succeeded");
        results.Accepted = true;
        results.Results = "Accepted";
        traceIdTable.Add(results);
        log.Info($"TraceId added for user");
    }
    catch
    {
        log.Info($"TraceId : {output.TraceId} failed");
        traceIdTable.Add(results);
        log.Info($"TraceId added for user");
    }
}