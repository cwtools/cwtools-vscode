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