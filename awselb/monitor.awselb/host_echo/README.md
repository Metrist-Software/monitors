# Test container for ELB monitor

This directory contains a minimal container to assist us in testing ELB responsiveness. All
it does it emit the hostname, so we know which container responded. This we can use to
see whether ELB has updated its target group.

The container gets published to Docker Hub through the Makefile; given that this app is so
simple that this is unlikely to ever change post the initial creation, this is a manual
process.
