namespace Main

open CWTools.Utilities.Position

module Line =
    // Visual Studio uses line counts starting at 0, F# uses them starting at 1
    let fromZ (line: Line0) = int line + 1

    let toZ (line: int) : Line0 =
        LanguagePrimitives.Int32WithMeasure(line - 1)

module PosHelper =
    let fromZ (line: Line0) idx = mkPos (Line.fromZ line) idx
    let toZ (p: pos) = (Line.toZ p.Line, p.Column)
