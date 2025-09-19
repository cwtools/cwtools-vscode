namespace Main

open CWTools.Utilities.Position

module PosHelper =
   let fromZ (line: Line0) idx = mkPos (Line.fromZ line) idx
   let toZ (p: pos) = (Line.toZ p.Line, p.Column)
