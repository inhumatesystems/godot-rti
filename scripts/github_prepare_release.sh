#!/bin/bash

VERSION=$1 ; shift

if [ -z "$VERSION" ]; then
    echo "usage: $0 <VERSION>"
    exit 1
fi

cd "$(dirname $0)/.."
set -e

git checkout $VERSION
git switch -c $VERSION-release

sed=sed
if [ "$(uname)" == "Darwin" ]; then
    sed=gsed
fi
gsed -i "s/0.0.1-dev-version/${VERSION}/g" project.godot addons/inhumate_rti/plugin.cfg addons/inhumate_rti/RTIConnection.cs
git add project.godot addons/inhumate_rti/plugin.cfg addons/inhumate_rti/RTIConnection.cs
git commit -m "Bump version ${VERSION}"

git log -n1

echo "OK last step manually!"
echo "git push github ${VERSION}-release"
