#an_anomaly_category = {				# Anomaly category ID key
#
#	should_ai_use = yes/no			# Allows AI empires to generate the category. Default: no
#	should_ai_and_humans_use = yes/no # If yes, both AI and human empires can use this anomaly (overrides should_ai_use)
#
#	desc = "key"					# Optional, if no desc is given "<category key>_desc" is assumed
#
#	desc = {						# Can also use triggered descs. First valid entry will be used.
#		trigger = { ... }			# Scope: planet, from = ship
#		text = "key"				# Localization key for description
#	}
#	picture = GFX_picture			# Picture displayed in category window
#	level = int						# Anomaly level, 1 to 10
#
#	null_spawn_chance = 0.5			# Default 0. 0.0 - 1.0 (0 to 100%) chance category will NOT spawn
#									# even if it is picked by the anomaly die roll. Used to make
#									# categories for unusual objects (e.g. black holes) actually rare.
#
#	max_once = yes/no				# default NO, if true will spawn category only once per empire
#	max_once_global = yes/no		# default NO, if true will spawn category only once per game
#
#	spawn_chance = {				# Chance for this anomaly category to spawn, 
#		base = <num>				# relative to other valid categories. Default: base = 0
#		modifier = {				# Spawn chance modifier
#			add/factor = <num>
#			<triggers>				# Scope: planet, from = ship
#		}
#	}
#
#	on_spawn = { <effects> }		# Executes immediately when anomaly category is spawned. 
#									# Scopes are this/root: planet, from: ship
#									# NOTE: on_spawn effects will not run if category is spawned through console
#
#	on_success = {					# Picks anomaly event to fire; similar to random_list
#		1 = {						# Base chance
#			max_once = yes			# Individual outcomes default to max_once = yes,
#			max_once_global = no	 # and max_once_global = no
#			modifier = {			# Optional modifiers
#				add/factor = <num>
#				<triggers>			# Scope: ship, from: planet
#			}
#			anomaly_event = <id>	# New effect anomaly_event fires specified event ID. Scope: ship, from: planet		   
#		}							# Can also use ship_event, though it gets different scopes:
#									# ship, from: ship, fromfrom: planet	   
#
#		1 = <event id>				# shorthand for 1 = { anomaly_event = <event id> }
#	}
#
#	on_success = <event id>			# Shorthand for on_success = { 1 = { anomaly_event = <event id> } }
#}									# Only use if there is only one outcome in the category