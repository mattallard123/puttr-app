#!/bin/bash
# Parity: puttr-app's putt maths (JavaScript, unchanged) against the C# port (AppPutt.cs).
#
#   Tools/parity/run.sh            test set made by Unity (or an existing Tools/parity/putts.json)
#   Tools/parity/run.sh -fresh     make the test set again
#
# To check real putts, export your own from the Mac build and run on them:
#   open the app with  -app2ExportPutts "$PWD/Tools/parity/real.json"  and sign in
#   cp Tools/parity/real.json Tools/parity/putts.json && Tools/parity/run.sh
P="$(cd "$(dirname "$0")/../.." && pwd)"
U=/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity
cd "$P"
"$U" -batchmode -quit -projectPath "$P" -executeMethod PuttrApp.EditorTools.Parity.MakePutts $1 -logFile Logs/parity-make.log || { grep "error CS" Logs/parity-make.log | head; exit 1; }
"$U" -batchmode -quit -projectPath "$P" -executeMethod PuttrApp.EditorTools.Parity.Judge -logFile Logs/parity-judge.log || { grep "error CS\|Exception" Logs/parity-judge.log | head; exit 1; }
grep "^Puttr 2 parity" Logs/parity-make.log Logs/parity-judge.log | sed 's/^.*log://'
node Tools/parity/app-side.mjs Tools/parity/putts.json Tools/parity/app.json || exit 1
python3 Tools/parity/compare.py Tools/parity
