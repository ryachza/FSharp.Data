﻿#if INTERACTIVE
#r "../../bin/FSharp.Data.dll"
#r "../../packages/NUnit.2.6.3/lib/nunit.framework.dll"
#r "../../packages/FsCheck.0.9.2.0/lib/net40-Client/FsCheck.dll"
#load "../Common/FsUnit.fs"
#else
module FSharp.Data.Tests.JsonParserProperties
#endif

open NUnit.Framework
open System
open FSharp.Data
open FsCheck

[<TestFixtureSetUp>]
let fixtureSetup() = 
    Runner.init.Force() |> ignore

#if INTERACTIVE
fixtureSetup()
#endif

let escaped = Set(['t';'r';'b';'n';'f';'\\';'"'])

let rec isValidJsonString s = 
    match s with
    | [] -> true
    | ['\\'] -> false
    | '"' :: t -> false
    | h :: t when Globalization.CharUnicodeInfo.GetUnicodeCategory h 
                    = Globalization.UnicodeCategory.Control -> false
    | '\\' :: 'u' :: d1 :: d2 :: d3 :: d4 :: t 
        when [d1;d2;d3;d4] |> Seq.forall 
            (fun c -> (Char.IsDigit c || Text.RegularExpressions.Regex("[a-fA-F]").IsMatch(c.ToString())))
                -> isValidJsonString t
    | '\\' :: i :: t when escaped |> (not << Set.contains i) -> false
    | '\\' :: i :: t when escaped |> Set.contains i -> isValidJsonString t
    | h :: t -> isValidJsonString t 

let validJsonStringGen = 
    Arb.generate<string> 
    |> Gen.suchThat ((<>) null)
    |> Gen.suchThat (Seq.toList >> isValidJsonString)

let jsonStringGen : Gen<string> =
 
    let validJsonStringGen' = 
        validJsonStringGen
        |> Gen.map (sprintf "\"%s\"")
    
    let boolGen = Gen.elements ["true"; "false"]
    let nullGen = Gen.constant "null"
    let numGen  = Arb.generate<decimal>
                  |> Gen.map (fun d -> d.ToString(System.Globalization.CultureInfo.InvariantCulture))

    let recordGen record =
        record
        ||> Gen.map2 (sprintf "{%s:%s}") 

    let rec tree() =
        let tree' s  =
            match s with
            | 0 -> Gen.oneof [validJsonStringGen'; boolGen; nullGen; numGen]
            | n when n>0 ->
                let subtree =
                    (validJsonStringGen', tree())
                    |> recordGen
                    |> Gen.resize (s|>float|>sqrt|>int)
                let arrayGen =
                    tree()
                    |> Gen.listOf
                    |> Gen.map (fun l -> sprintf "[%s]" (String.Join(",",l)))
                    |> Gen.resize (s|>float|>sqrt|>int) 
                Gen.oneof [ subtree;  arrayGen; validJsonStringGen'; boolGen; nullGen; numGen]
            | _ -> invalidArg "s" "Only positive arguments are allowed"
        Gen.sized tree'

    (validJsonStringGen', tree())
    |> recordGen

[<Test>]
let ``Stringifing parsed string returns the same string`` () =
    let stringifyParsed (s : string) =
        let jsonValue = JsonValue.Parse s
        jsonValue.ToString(JsonSaveOptions.DisableFormatting) = s
    let jsonStringArb = Arb.fromGen (jsonStringGen)
    
    Check.One ({Config.QuickThrowOnFailure with MaxTest = 10000},
              (Prop.forAll jsonStringArb stringifyParsed))