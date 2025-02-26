﻿[<AutoOpen>]
module Vide.Maui

open System
open System.Collections.Generic
open Microsoft.Maui
open Microsoft.Maui.Controls
open Vide
open Vide.WpfishModel

// The current basis for vide-implementations is the
// NodeModel (with NodeDocument and NodeContext), which
// actually works like a true node model (always many children).
// The node model doesn't distinguish between different container
// forms (e.g. single / multi content), so there are some casts
// needed here and runtime exceptions can occur. Anyway, content models can be modeled correctly at the API
// surface, using the Render-builders.
type MauiDocument() =
    let makeEx parent child actionName =
        let childTypeName = match child with Some child -> child.GetType().Name | None -> "-"
        Exception $"Cannot perform '{actionName}' to node of type {parent.GetType().Name} (child type = {childTypeName})."
    interface INodeDocument<IView> with
        member _.AppendChild(parent, child) =
            match box parent with
            | :? ICollection<IView> as viewColl -> viewColl.Add(child)
            | :? ContentView as contentView -> contentView.Content <- child :?> View
            | _ -> raise <| makeEx parent (Some child) "append child"
        member _.RemoveChild(parent, child) =
            match box parent with
            | :? ICollection<IView> as viewColl -> viewColl.Remove(child) |> ignore
            | :? ContentView as contentView -> contentView.Content <- null
            | _ -> raise <| makeEx parent (Some child) "remove child"
        member _.GetChildren(parent) =
            match box parent with
            | :? ICollection<IView> as viewColl -> viewColl |> Seq.toList
            | :? ContentView as contentView -> contentView.Content :> IView |> List.singleton
            | _ -> []
        member _.ClearChildren(parent) =
            match box parent with
            | :? ICollection<IView> as viewColl -> viewColl.Clear()
            | :? ContentView as contentView -> contentView.Content <- null
            | _ -> raise <| makeEx parent None "clear content"
        member _.CreateTextNode(text) =
            let tn = Label()
            do tn.Text <- text
            let textNode =
                {
                    node = tn :> IView
                    getText = fun () -> tn.Text
                    setText = fun value -> tn.Text <- value
                }
            textNode
    interface IWpfishDocument<IView> with
        member _.CreateNodeOfType<'e when 'e : (new: unit -> 'e)>() =
            let e = new 'e()
            e, (box e) :?> IView

type MauiContext(parent: IView) =
    inherit WpfishContext<IView>(parent, MauiDocument())
    static member Create<'e when 'e :> IView>(thisNode: 'e) = MauiContext(thisNode)


// --------------------------------------------------
// Specialized builder definitions
// --------------------------------------------------

type ComponentRetCnBuilder() =
    inherit ComponentRetCnBaseBuilder<IView,MauiContext>()

type RenderValC0Builder<'v,'e when 'e :> IView>(createThisElement, checkChildNode, createResultVal) =
    inherit RenderValC0BaseBuilder<'v,'e,IView,MauiContext>(MauiContext.Create, createThisElement, checkChildNode, createResultVal)

type RenderRetC0Builder<'e when 'e :> IView>(createThisElement, checkChildNode) =
    inherit RenderRetC0BaseBuilder<'e,IView,MauiContext>(MauiContext.Create, createThisElement, checkChildNode)

type RenderValC1Builder<'v,'e when 'e :> IView>(createThisElement, checkChildNode, createResultVal) =
    inherit RenderValC1BaseBuilder<'v,'e,IView,MauiContext>(MauiContext.Create, createThisElement, checkChildNode, createResultVal)

type RenderRetC1Builder<'e when 'e :> IView>(createThisElement, checkChildNode) =
    inherit RenderRetC1BaseBuilder<'e,IView,MauiContext>(MauiContext.Create, createThisElement, checkChildNode)

type RenderValCnBuilder<'v,'e when 'e :> IView>(createThisElement, checkChildNode, createResultVal) =
    inherit RenderValCnBaseBuilder<'v,'e,IView,MauiContext>(MauiContext.Create, createThisElement, checkChildNode, createResultVal)

type RenderRetCnBuilder<'e when 'e :> IView>(createThisElement, checkChildNode) =
    inherit RenderRetCnBaseBuilder<'e,IView,MauiContext>(MauiContext.Create, createThisElement, checkChildNode)


// --------------------------------------------------
// Specialized vide functions
// --------------------------------------------------

//module Vide =

//    // all this because of value restrictions / quite redundant with Fable?

//    [<GeneralizableValue>]
//    let mauiContext : Vide<MauiContext,unit,MauiContext> =
//        Vide <| fun s ctx -> ctx,None

//    [<GeneralizableValue>]
//    let node<'n when 'n :> IView> : Vide<'n,unit,MauiContext> =
//        Vide <| fun s ctx ->
//            // TODO: OUCH!!! Was ist da los - wieso bekomme ich das nicht besser hin?
//            ctx.Parent :?> 'n,None

module VideApp =
    module Maui =
        let inline doCreate appCtor (host: #ContentView) (content: Vide<'v,'s,MauiContext>) onEvaluated =
            let content = RenderRetC1Builder((fun _ -> host), fun _ -> Keep) { content }
            let ctxCtor = fun () -> MauiContext(host)
            appCtor content ctxCtor onEvaluated
        let create host content onEvaluated =
            doCreate VideApp.create host content onEvaluated
        let createWithUntypedState host content onEvaluated =
            doCreate VideApp.createWithUntypedState host content onEvaluated

let vide = ComponentRetCnBuilder()
