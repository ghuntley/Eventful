﻿namespace BookLibrary

open System
open BookLibrary.Aggregates

type BookId = {
    Id : Guid
}

[<CLIMutable>]
type AddBookCommand = {
    BookId : BookId
    Title : string
}

[<CLIMutable>]
type BookAddedEvent = {
    BookId : BookId
    Title : string
}

type BookEvents = 
    | Added of BookAddedEvent

module Book =
    let getStreamName () (bookId : BookId) =
        sprintf "Book-%s" <| bookId.Id.ToString("N")

    let validateCommand (cmd : AddBookCommand) : seq<string option * string> =
       match (Choice1Of2 "andrew") with
       | Choice1Of2 _ -> Seq.empty
       | Choice2Of2 errors -> errors |> FSharpx.Collections.NonEmptyList.toSeq

    let cmdHandlers = 
        seq {
           let addBook (cmd : AddBookCommand) =
               Added { 
                   BookAddedEvent.BookId = cmd.BookId
                   Title = cmd.Title
               }

           yield buildCmdHandler addBook
        }

    let bookIdGuid (bookId : BookId) = bookId.Id
    let handlers () =
        Eventful.Aggregate.toAggregateDefinition getStreamName getStreamName bookIdGuid cmdHandlers Seq.empty

open System.Web
open System.Net.Http
open System.Web.Http
 
type BooksController() =
    inherit ApiController()
 
    // GET /api/values
    member x.Get() = [| "value1"; "value2" |] |> Array.toSeq

    // GET /api/values/5
    member x.Get (id:int) = "value"

    // POST /api/values
    member x.Post ([<FromBody>] value:string) = ()

    // PUT /api/values/5
    member x.Put (id:int) ([<FromBody>] value:string) = ()

    // DELETE /api/values/5
    member x.Delete (id:int) = ()