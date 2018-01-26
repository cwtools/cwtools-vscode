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