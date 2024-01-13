#!/usr/bin/env bash

set -eo pipefail

echo "Invalidating cloudfront monitor distributions"

aws cloudfront create-invalidation --distribution-id E2DUDS5AEBTWMN --paths '/*' --no-cli-pager # canarymonitor.com distribution
aws cloudfront create-invalidation --distribution-id E1BQVBNF9P16DM --paths '/*' --no-cli-pager # metrist.io distribution
