
# TODO base code (core, webhooks, runner)
# TODO npm_packages?

SHARED := Metrist.Core Metrist.Webhooks Metrist.Runner

DOTNET_MONITORS := $(wildcard */Metrist.Monitors.*)
OTHER_MONITORS := $(wildcard */monitor.*)

SUBDIRS = $(SHARED) $(DOTNET_MONITORS) $(OTHER_MONITORS)

# - Compile is the minimal thing, just run source code
# through the compiler, essentially.
# - Build is a bit more complete, it should create executables
# for example.
# - Package makes final distribution artifacts.
# - Clean removes build artifacts.
RECURSIVE_TARGETS = compile build package clean

.PHONY: all $(RECURSIVE_TARGETS) $(SUBDIRS)

all: compile

$(RECURSIVE_TARGETS):
	$(MAKE) TARGET=$@ recurse

# For developers: set up a development environment.
# `make dev test` should "prove" that you're read for
# developent.
dev:
	-cat .tool-versions | awk '{print $$1}' | xargs -n1 asdf plugin add
	asdf install

recurse: $(SUBDIRS)

$(SUBDIRS):
	$(MAKE) -C $@ $(TARGET)

# Make sure that parallel compilation works.
$(DOTNET_MONITORS): $(SHARED)
