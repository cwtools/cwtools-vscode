# 0.4.8
* Add localisation checking for war goals
* Add localisation checking for custom\_tooltip everywhere
# 0.4.7
* Correctly validate effects/triggers inside if, while and event_targets
# 0.4.6
* Fix bug with event\_target check when you reference an event that doesn't exist
# 0.4.5
* Improve event\_target checking to handle loops (still behind experimental setting)
# 0.4.4
* Add file "ignore" list to filter filenames that won't return validation errors
* Stop validation "random_names" as it's so non-standard
# 0.4.3
* Improve responsiveness of validation when make rapid changes (by only checking the latest version of a file not every changed version)
* Add scripted_effect event_target checking (still behind experimental setting)
# 0.4.2
* Add (experimental) event_target checking (hidden behind experimental flag)
* Add tooltip info for scripted_effects/triggers (taken from comments above definition)
# 0.4.1
* Add (experimental) modifier existence and scope checking for:
    * Buildings
    * Agendas
    * Ascension perks
    * Component templates
    * Edicts
    * Ethics
    * Governments
    * Policies
    * Ship sizes
    * Species rights
    * Starbase buildings
    * Starbase modules
    * Strategic resources
    * Technologies
    * Tradition categories
    * Traditions
    * Traits
# 0.4.0
* Add OSX support
* Update embedded interface to 2.0
* Significantly improve performance for large mods
* Add effect and trigger validation for:
    * Anomalies
    * Armies
    * Ascension perks
    * Bombardment stances
    * Buildable pops
    * Buildings
    * Bypasses
    * Casus belli
    * Diplomatic actions
    * Edicts
    * Ethics
    * Mandates
    * Megastructures
    * Observation stations
    * Personalities
    * Policies
    * Pop faction types
    * Ship sizes
    * Solar system initializers
    * Species rights
    * Starbase buildings
    * Starbase modules
    * Starbase types
    * Start screen messages
    * Subjects
    * System types
    * Technology
    * Terraform
    * Tradition categories
    * Traditions
    * Traits
    * War goals
# 0.3.3
* Fix bug with scope usage through prev
* Add simple scope commands to autocomplete
* Properly parse hsv/rgb with 4 values
# 0.3.2
* Check scope usage through "AND", "OR", etc
* Properly parse files with only values
* Properly parse "rgb"
* Give better feedback for parser errors when there is no matched brace
# 0.3.1
* Update localisation to 2.0
# 0.3.0
* Add simple autocomplete (triggers, effects, and modifiers)
* Add documentation for autocomplete (usage information and supported scopes)
* Add action to generate .yml for missing localisation keys
* Ignore missing keys that are just scripted loc (e.g. "[GetName]")
* Check correct scope of static modifiers use in has_modifier and remove_modifier
# 0.2.19
* Support scripted_variables
* Fix bug with checking variables in nested blocks
* Check correct scope of static modifiers use in add_modifier effects
# 0.2.18
* Add Stellaris 2.0 scopes
* Set 2.0 trigger_docs as default
# 0.2.17
* Check effectFile and textureFile against actual files, throw an error if the file isn't found
# 0.2.16
* Add "cwtools.errors.ignore" setting to allow ignoring of specific error codes
* Handle "hidden" prefix on scopes
# 0.2.15
* Update scope checking to support PREV and check inside scopes
* Check effects and triggers in more parts of events (options, desc triggers)
# 0.2.14
* Add experimental option to enable experimental features
* Added 2.0 effects/triggers behind experimental flag
# 0.2.13
* Check button_effects used in .gui files are defined
* Check spriteTypes used in .gui files are defined
# 0.2.12
* Fix ambient_object localisation checks
* Support recursive triggers and effects
# 0.2.10
* Add localisation_synced checking in events
# 0.2.9
* Add localisation for pop\_faction\_types
    * pft
    * pft plus "_desc"
* Add localisation for static_modifiers
    * modifier
    * modifier plus "_desc"
* Add localisation for spaceport modules
    * "sm_" plus module
* Add localisation for traits
    * trait
    * trait plus "_desc"
* Add localisation for governments
    * government key
    * ruler\_title, ruler\_title\_female, heir\_title, heir\_title\_female
    * civic
    * civic plus "_desc"
    * civic description
* Add localisation for personalities
    * "personality_" plus personality
    * peronality plus "_desc"
* Add localisation for ethics
    * ethics
    * ethics plus "_desc"
* Add localisation for planet_classes
    * planet class
    * planet class plus "_desc"
    * if colonizable
        * planet class plus "_tile"
        * planet class plus "\_tile\_desc"
        * "trait\_" plus planet class plus "\_preference"
        * preference plus "_desc"
        * planet class plus "_habitability"
* Add localisation for edicts
    * "edict\_" plus edict name
    * "edict\_" plus edict name plus "\_desc"
* Add localisation for policies
    * "policy\_" plus policy
    * "policy\_" plus policy plus "\_desc"
    * policy option name
    * policy option name plus "\_desc"
    * policy option flags
* Add more localisation for technology
    * feature_flags
    * feature\_flags + "\_desc"
* Add localisation for section_templates
    * key
* Add localisation for species\_name
    * "\_desc"; "\_plural"; "\_insult\_01"; "\_insult\_plural\_01"; "\_compliment\_01";"\_compliment\_plural\_01";"\_spawn";"\_spawn\_plural";
                                "\_sound\_01";"\_sound\_02";"\_sound\_03";"\_sound\_04";"\_sound\_05";"\_organ";"\_mouth"
* Add localisation for strategic_resources
    * resource
    * resource + "\_desc"

# 0.2.8
* Temporarily remove research leader checks
# 0.2.7
* Add localisation for armies and army_attachments
    * army name
    * army name plus "_plural"
    * army name plus "_desc"
    * attachtment is the same three but starting "army_"
* Add localisation for aura in component_templates
* Add localisation for diplo phrases
* Check technology "research_leader"'s "has\_trait" matches the technology category
* Add localisation for ship_sizes
* Add localisation for pop\_faction\_types
* Add localisation for technology gateway
* Add localisation for species_rights
    * right name
    * right name plus "_tooltip"
    * right name plus "\_tooltip\_delayed"
* Add localisation for map setup_secnarios
* Add localisation for megastructurew
    * megastructure name
    * megastrcture name plus "_DESC"
    * megastructure name plus "\_MEGASTRUCTURE\_DETAILS"
    * megastructure name plsu "\_CONSTRUCTION\_INFO\_DELAYED"
# 0.2.6
* Add more validation for technologies
    * All "research_leader" must have an area, which should match the technology
# 0.2.5
* Add localisation checks for buildings
    * building name
    * build name plus "_desc"
    * all "fail_text" under buildings
* Add localisation checks for component_templates
    * key
* Add localisation checks for traditions
    * use tradition_categories to determine traditions
    * tradition name for all
    * tradition_desc for start + traditions
    * tradition_delayed for traditions
    * tradition_effect for start and finish

# 0.2.4
* Add localisation checks for technology
    * technology name
    * technology name plus "_desc"
    * all "title" and "desc" keys under "prereqfor_desc"
* Add localisation checks for component_sets
    * component\_set's "key", but only is "required\_component\_set" is false