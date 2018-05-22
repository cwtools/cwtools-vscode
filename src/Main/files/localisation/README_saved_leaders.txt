# Saved Leaders
# The saved leader effect can be used to create a fake character for narrative purposes in events. 

#leader creation attribs:
#    event_leader = <yes/no> // Decides if the leader is eligible for election. Event leaders are not.
#    immortal = <yes/no>
#    type = <admiral/general/scientist/governor/ruler/random>
#    species = <last_created,owner_main_species or event target>
#    name = <random or string> // Name is assigned in the synced localisation file.
#    skill = <int>
#    traits = { <key/random_trait>*  }
#    sub_type = <key?>
#    gender = <male/female/indeterminable>
#    set_age = <int>
#    leader_age_min = <int>
#    leader_age_max = <int>
#
#Creates a new saved leader (per country)
#CountryScope
#create_saved_leader = {
#    creator = <target>                 // Used for generation of the leader, defaut is same as country in scope.
#    key = <string>                 // Referal key from localization etc. Can also be used as right-hand argument in picture_event_data = { portrait = <key>}
#    <leader creation attribs>*        // as explained above, shares this with create_leader.
#}
#
#Removes a saved leader and kills it (per country)
#//CountryScope
#remove_saved_leader = <key>
#
#Moves a saved leader into the active leaders that a player can use, thus removes it as a saved leader (per country)
#CountryScope
#activate_saved_leader = {
#    add_to_owned = <yes/no>        // default = yes, if no it will be added to the leaderpool instead of directly recruited.
#    key = <string>                // key for saved leader.
#    effect = { <effect>* }            // effect to run upon the leader after being added to the country, same as effect in create_leader
#}