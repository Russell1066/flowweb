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

const int PACIFIERTIME = 20 * 1000; // miliseconds
const int MAXMACHINES = 200;

public static void Run(string myQueueItem,
    CloudTable traceIds,
    IAsyncCollector<string> outputQueueItems,
    TraceWriter log)
{
    var logger = new Logger(log, myQueueItem);
    var traceId = myQueueItem;

    logger.Info($"Queue trigger function processing : {myQueueItem} (should show twice)");
    var retrieve = traceIds.Execute(TableOperation.Retrieve<UploadResults>("0", traceId));
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

    // It shouldn't be in the table if it isn't already valid
    if (!boardDescription.IsValid())
    {
        results.Results = $"board failures reports {boardDescription.DescribeFailures()}";
        results.ProcessEndTime = DateTime.Now.ToString("o");
        traceIds.Execute(TableOperation.Merge(results));
        logger.Error(results.Results);
        return;
    }

    var board = new FlowBoard();
    board.InitializeBoard(boardDescription);
    Solver2.SolverData solverData = new Solver2.SolverData()
    {
        BoardDefinition = Game.Board,
        MaxNodes = MAXMACHINES,
    };

    foreach (var index in Solver2.GetSolutionList(board, MAXMACHINES))
    {
        solverData.SolutionIndex = index;
        var wrapper = SolverMgr.GetSolverWrapper(solverData, traceId);
        outputQueueItems.Add(wrapper);
    }
}