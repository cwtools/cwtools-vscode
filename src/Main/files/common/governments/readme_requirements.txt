# Government Requirements
# -----------------------
#
# Government authorities and civics use a custom list syntax instead of normal
# triggers in potential and possible to specify valid combinations:
#
#
#	possible = {
#
#		ethics = {
#			# All of these are required:
#			value = ethic_1
#			value = ethic_2
#
#			# One of these is required:
#			OR = {
#				text = translation_key		# optional, overrides the auto-generated tooltip text
#				value = ethic_3
#				value = ethic_4
#			}
#
#			# This one must not be present:
#			NOT = {
#				text = translation_key		# optional
#				value = ethic_5
#				# May contain only one value!
#			}
#
#			# None of these must be present:
#			NOR = {
#				text = translation_key		# optional
#				value = ethic_6
#				value = ethic_7
#			}
#		}
#
#		country_type = { ... }
#
#		authority = { ... }
#
#		civics = { ... }
#
#		text = translation_key				# optional
#	}
#
#
# Authorities support:
#   - country_type
#   - ethics
#
# Civics support:
#   - country_type
#   - ethics
#   - authority
#   - civics
#
# Species classes support:
#   - country_type
#   - ethics
#   - authority
#   - civics
#