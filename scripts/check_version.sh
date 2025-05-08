#!/bin/bash

# Checks that, for releases, the tag version corresponds to the version in package.json etc

version=$1 ; shift
if [ -z "$version" ]; then 
    echo "usage: $0 <version>"
    exit 2
fi

if grep -q "config/version=\"${version}\"" project.godot ;\
    grep -q "version=\"${version}\"" addons/inhumate_rti/plugin.cfg ;\
    grep -q "Version = \"${version}\"" addons/inhumate_rti/src/RTIConnection.cs ; then
    echo "Version check ok"
else
    echo "Version check fail - fix project.godot, plugin.cfg and RTIConnection.cs"
    exit 1
fi
