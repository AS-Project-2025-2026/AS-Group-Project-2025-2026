#!/bin/bash
set -e
cd "$(dirname "$0")"
latexmk -pdf report.tex
latexmk -c
