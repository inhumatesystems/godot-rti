extends Node

@export var app_label: Label
@export var version_label: Label
@export var setup_scenarios_label: Label

func _ready():
	app_label.text = RTI.Application
	version_label.text = RTI.ApplicationVersion
	if len(RTI.Scenarios) > 0 or len(RTI.ScenarioNames) > 0:
		setup_scenarios_label.visible = false
	
