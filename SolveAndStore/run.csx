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
    public string PartitionKey { get; }
    public string RowKey { get; }
    public string TraceId { get; set; }
    public string Name { get; set; }
    public FlowBoard.BoardDefinition Board { get; set; }
    public BoardTable()
    {
        PartitionKey = "0000";
        RowKey = Guid.NewGuid().ToString();
    }
};

public static void Run(string myQueueItem, ICollector<BoardTable> outputTable, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed: {myQueueItem}");

    var wrapper = JsonConvert.DeserializeObject<BoardWrapper>(myQueueItem);

    // It can't be in the queue if it isn't alread valid!
    if(!wrapper.Board.IsValid())
    {
        log.Info($"board failures reports {wrapper.Board.DescribeFailures()}");
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
    catch (OperationCanceledException cancelled)
    {
        log.Error(cancelled.ToString());
        log.Info($"TraceId : {wrapper.TraceId} failed");
        return;
    }
    catch (Exception cancelled)
    {
        log.Error($"Exception Type: {cancelled.GetType().Name}");
        log.Error(cancelled.ToString());
        log.Info($"TraceId : {wrapper.TraceId} failed");
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
    }
    catch
    {
        log.Info($"TraceId : {output.TraceId} failed");
    }
}