extends Control

const CLICK_INCREMENT: int = 1

var score: int = 0

@onready var score_value: Label = %ScoreValue


func _ready() -> void:
	$Center/Panel/VBox/ScoreButton.pressed.connect(_on_score_button_pressed)
	_update_score()


func _on_score_button_pressed() -> void:
	score += CLICK_INCREMENT
	_update_score()


func _update_score() -> void:
	score_value.text = str(score)
