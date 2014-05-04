﻿namespace Eventful.Tests

open Eventful
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit
open FsCheck
open FsCheck.Xunit
open System

module WorktrackingQueueTests = 

    [<Fact>]
    let ``Test speed of simple items`` () : unit =
        let groupingFunction = Set.singleton << fst

        let work = (fun _ _ -> Async.Sleep(10))
        let worktrackingQueue = new WorktrackingQueue<int,(int * int)>(groupingFunction, work, 100000, 50)

        let items = 
            [0..10000] |> List.collect (fun group -> [0..10] |> List.map (fun item -> (group, item)))

        for item in items do
            worktrackingQueue.Add item |> Async.RunSynchronously

        worktrackingQueue.AsyncComplete () |> Async.RunSynchronously
        
    [<Fact>]
    let ``Completion function is called when item complete`` () : unit =
        let groupingFunction = Set.singleton << fst

        let tcs = new TaskCompletionSource<bool>()

        let completedItem = ref ("blank", "blank")
        let complete item = async {
            do! Async.Sleep(100)
            completedItem := item
            tcs.SetResult true
        }

        let worktrackingQueue = new WorktrackingQueue<string,(string * string)>(groupingFunction, (fun _ _ -> Async.Sleep(100)), 100000,  10,  complete)
        worktrackingQueue.Add ("group", "item") |> Async.Start

        tcs.Task.Wait()

        !completedItem |> fst |> should equal "group"
        !completedItem |> snd |> should equal "item"

    [<Fact>]
    let ``Completion function is called immediately when an items resuls in 0 groups`` () : unit =
        log4net.Config.XmlConfigurator.Configure()
        let groupingFunction _ = Set.empty

        let tcs = new TaskCompletionSource<bool>()

        let completedItem = ref ("blank", "blank")
        let complete item = async {
            do! Async.Sleep(100)
            completedItem := item
            tcs.SetResult true
        }

        let worktrackingQueue = new WorktrackingQueue<string,(string * string)>(groupingFunction, (fun _ _ -> Async.Sleep(100)), 100000,  10,  complete)
        worktrackingQueue.Add ("group", "item") |> Async.Start

        tcs.Task.Wait()

        !completedItem |> fst |> should equal "group"
        !completedItem |> snd |> should equal "item"

    [<Fact(Skip = "The new design doesn't allow this behaviour")>]
    let ``Add item throws if grouping function throws`` () : unit =
        let groupingFunction _ = failwith "Grouping function exception"

        let worktrackingQueue = new WorktrackingQueue<string,(string * string)>(groupingFunction, (fun _ _ -> Async.Sleep(100)), 100000,  10)
        (fun () -> worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously  |> ignore) |> should throw typeof<System.Exception>

    [<Fact>]
    let ``Can run multiple items`` () : unit =
        let groupingFunction = Set.singleton << fst

        let completedItem = ref ("blank", "blank")
        let complete item = async {
            do! Async.Sleep(100)
            completedItem := item
        }

        let work (group:string) items = async {
                System.Console.WriteLine("Work item {0}", group)
            }

        let worktrackingQueue = new WorktrackingQueue<string,(string * string)>(groupingFunction, work, 100000, 10, complete)
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously
        worktrackingQueue.Add ("group", "item") |> Async.RunSynchronously

        worktrackingQueue.AsyncComplete () |> Async.RunSynchronously

    [<Fact>]
    let ``Given item split into 2 groups When complete Then Completion function is only called once`` () : unit =
        let groupingFunction _ = [1;2] |> Set.ofList

        let completeCount = new CounterAgent()

        let complete item = async {
            // do! Async.Sleep(100)
            do! completeCount.Incriment 1
        }

        async {
            let worktrackingQueue = new WorktrackingQueue<int,(string * string)>(groupingFunction, (fun _ _ -> Async.Sleep(100)), 100000, 10, complete)
            do! worktrackingQueue.Add ("group", "item")
            do! worktrackingQueue.AsyncComplete()
            do! Async.Sleep 100
            let! count = completeCount.Get()
            count |> should equal 1
        } |> Async.RunSynchronously

    [<Fact>]
    let ``Given empty queue When complete Then returns immediately`` () : unit =
        let worktrackingQueue = new WorktrackingQueue<unit,string>( (fun _ -> Set.singleton ()),(fun _ _ -> Async.Sleep(1)), 100000, 10,(fun _ -> Async.Sleep(1)))
        worktrackingQueue.AsyncComplete() |> Async.RunSynchronously

    [<Property>]
    let ``When Last Item From Last Group Complete Then Batch Complete``(items : List<(Guid * Set<int>)>) =
        let state = WorktrackQueueState<int, obj>.Empty
        let allAddedState = items |> List.fold (fun (s:WorktrackQueueState<int, obj>) (key, groups) -> s.Add(key, groups, async { return ()}) ) state
        let replyChannel = new obj()
        let (batchCreated, batchCreatedState) = allAddedState.CreateBatch(replyChannel)

        let completeMessages =
            items 
            |> List.collect (fun (item, groups) -> groups |> Set.map (fun g -> (item, g)) |> Set.toList)

        let applyComplete (_,_, queueState:WorktrackQueueState<int, obj>) (key, group) = 
            queueState.ItemComplete(group, key)
            
        match (batchCreated, completeMessages) with
        | (false, []) -> true
        | (false, _) -> false
        | (true,_) -> 
            let startState = (None, List.empty, batchCreatedState)
            let allCompleteState = 
                completeMessages 
                |> List.fold applyComplete startState

            match allCompleteState with
            | (_, [singleBatch], _) when singleBatch = replyChannel -> true
            | _ -> false