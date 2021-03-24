#!/bin/sh

set -e

for i in win linux osx
	do
	dotnet publish PacsTest -o $i -r $i-x64 -p:SelfContained=true -p:PublishSingleFile=true
done
