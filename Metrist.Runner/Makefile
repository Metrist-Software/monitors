
include ../Makefile.common
include ../Makefile.dotnet

# Gives a somewhat ugly override warning but it's the simplest solution.
build:
	dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true --self-contained true
