﻿namespace Eventful.Raven

open System
open System.Threading
open System.Runtime.Caching

open Eventful
open Metrics

open FSharpx

open Raven.Client
open Raven.Abstractions.Data
open Raven.Json.Linq
open FSharp.Collections.ParallelSeq

type RavenReplayProjector<'TMessage when 'TMessage :> IBulkRavenMessage> 
    (
        documentStore:Raven.Client.IDocumentStore, 
        documentProcessor:DocumentProcessor<string, 'TMessage>,
        databaseName: string
    ) =

    let numWorkers = 10

    let fetcher = {
        new IDocumentFetcher with
            member x.GetDocument<'TDocument> key : Tasks.Task<ProjectedDocument<'TDocument> option> = 
                async {
                    return None 
                } |> Async.StartAsTask
            member x.GetDocuments request = 
                async {
                    return
                        request
                        |> Seq.map(fun (a,b) -> (a,b,None))
                } |> Async.StartAsTask

            member x.GetEmptyMetadata<'TDocument> () =
                RavenOperations.emptyMetadataForType documentStore typeof<'TDocument>
    }

    let mutable messages : 'TMessage list = List.Empty

    let documentsWithKeys msg =
        documentProcessor.MatchingKeys msg
        |> Seq.map (fun k -> (k,msg))

    let accumulateItems s (key, items) = async {
        let events = items |> Seq.map snd
        let! writeRequests = documentProcessor.Process(key, fetcher, events).Invoke() |> Async.AwaitTask
        return Seq.append s writeRequests
    }

    member x.Enqueue (message : 'TMessage) =
        messages <- message::messages

    member x.ProcessQueuedItems() =
        let inserts = 
            messages
            // reverse messages so they run in order
            |> List.rev
            |> PSeq.map documentsWithKeys
            // route events to documents
            |> Seq.collect id
            // group events into groups by document
            |> Seq.groupBy fst
            // group items into numWorkers batches
            |> PSeq.mapi(fun i x -> (i % numWorkers,x))
            |> PSeq.groupBy fst
            // map to async tasks
            |> PSeq.map (fun (_, workItems) -> async {
                let docs = 
                    workItems
                    |> Seq.map snd
                return! Async.foldM accumulateItems Seq.empty docs
            })
            |> Async.Parallel
            |> Async.RunSynchronously
            |> Array.toSeq
            |> Seq.collect id

        use bulkInsert = documentStore.BulkInsert(databaseName)
        for insert in inserts do
            match insert with
            | Write (writeRequest, _) ->
                let doc = RavenJObject.FromObject(writeRequest.Document, documentStore.Conventions.CreateSerializer())
                bulkInsert.Store(doc, writeRequest.Metadata.Force(), writeRequest.DocumentKey)
            | _ -> () // don't do anything for other operation types