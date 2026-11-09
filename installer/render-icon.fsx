#r "nuget: Svg, 3.4.7"

// Renders the Beam brand SVG (src/Beam.Tray/Assets/high-beam.svg) to PNG rasters
// with Svg.NET, so the vector file stays the single source of truth for every
// raster icon (app.ico entries, src/Beam.Tray/Assets/high-beam.png, assets/beam-256.png).
//
// Usage (args after `--`):
//   dotnet fsi render-icon.fsx -- --svg <file.svg> --out <file.png> --size 256 [--no-halo]
//   dotnet fsi render-icon.fsx -- --svg <file.svg> --out-dir <dir> --sizes 16,32,256 [--no-halo]
//
// --no-halo hides the element with id="halo" (the deep edge behind the brand fill),
// used for the plain-cyan assets/beam-256.png.

open System
open System.IO
open Svg

let args = fsi.CommandLineArgs |> Array.skip 1

let arg (name: string) : string option =
    match args |> Array.tryFindIndex (fun a -> a = name) with
    | Some i when i + 1 < args.Length -> Some args[i + 1]
    | _ -> None

let svgPath =
    match arg "--svg" with
    | Some p -> p
    | None -> failwith "missing --svg <path>"

let noHalo = Array.contains "--no-halo" args

/// Renders the SVG at px x px and returns PNG bytes.
let render (px: int) : byte[] =
    let doc = SvgDocument.Open<SvgDocument>(svgPath)
    if noHalo then
        match doc.GetElementById<SvgElement>("halo") with
        | null -> ()
        | halo -> halo.Display <- "none"
    doc.Width <- SvgUnit(float32 px)
    doc.Height <- SvgUnit(float32 px)
    use bmp = doc.Draw()
    // Saving straight to a file path trips a GDI+ ExternalException; via a
    // MemoryStream it works.
    use ms = new MemoryStream()
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png)
    ms.ToArray()

let writePng (path: string) (px: int) =
    let bytes = render px
    File.WriteAllBytes(path, bytes)
    printfn "WROTE %s %d %d" path px bytes.Length

match arg "--out", arg "--out-dir" with
| Some outFile, None ->
    let size =
        match arg "--size" with
        | Some s -> int s
        | None -> failwith "--out requires --size <px>"
    writePng outFile size
| None, Some outDir ->
    let sizes =
        match arg "--sizes" with
        | Some s -> s.Split(',') |> Array.map (fun x -> int (x.Trim()))
        | None -> failwith "--out-dir requires --sizes <csv>"
    Directory.CreateDirectory(outDir) |> ignore
    for s in sizes do
        writePng (Path.Combine(outDir, sprintf "icon-%d.png" s)) s
| _ -> failwith "give exactly one of --out <file> --size <px> or --out-dir <dir> --sizes <csv>"
