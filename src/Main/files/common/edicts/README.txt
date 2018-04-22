Special variables for Edicts (Country and Empire):
#  cost, base cost as in resource(s) and amount for activating the edict.

Special variables for Unity Ambitions where the cost is based on the next tradition unlock cost:
#  unity_cost_mult, cost multiplier for a specific ambition ( 0.1 = 90% discount of the total unity cost etc).

Shared variables for both Edicts and Unity Ambitions:
#  name, the name of the edict, also serves as localisation key.
#  potential, planet or country trigger to decide whether the edict should be shown.
#  allow, planet or country trigger to decide if whether this edict can be activated.
#  effect, an effect that is executed when this trigger activates.
#  prerequisites, tech requirements for this trigger.
#  ai_will_do, determines AI scoring for edict
#  modifier, a list of modifiers
#  length, number of days the trigger is active.