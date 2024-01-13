
VERSION := $(shell npm list|head -1|sed -e 's/.*@//' -e 's/ .*//')
CONTAINER := canarymonitor/host_echo:${VERSION}

all: build push

build:
	docker build -t ${CONTAINER} .

push:
	docker push ${CONTAINER}
