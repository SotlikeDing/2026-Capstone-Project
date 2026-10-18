import 'package:flutter/material.dart';

import '../services/public_trophy_service.dart';
import '../theme/app_theme.dart';
import '../widgets/hunting_card.dart';
import '../widgets/primary_button.dart';
import 'leaderboard_results_screen.dart';

const _allCategoriesValue = '__all_categories__';
const _legacyCategoryValue = '__legacy_category__';
const _allSpeciesValue = '__all_species__';
const _allFirearmTypesValue = '__all_firearm_types__';

class LeaderboardFilterScreen extends StatefulWidget {
  const LeaderboardFilterScreen({super.key, this.publicTrophyApi});

  final PublicTrophyApi? publicTrophyApi;

  @override
  State<LeaderboardFilterScreen> createState() =>
      _LeaderboardFilterScreenState();
}

class _LeaderboardFilterScreenState extends State<LeaderboardFilterScreen> {
  late final PublicTrophyApi _publicTrophyService;
  late Future<LeaderboardFilterOptions> _filterOptionsFuture;

  String _selectedCategory = _allCategoriesValue;
  String _selectedSpecies = _allSpeciesValue;
  String _selectedFirearmType = _allFirearmTypesValue;
  int _selectionVersion = 0;

  @override
  void initState() {
    super.initState();
    _publicTrophyService = widget.publicTrophyApi ?? PublicTrophyService();
    _filterOptionsFuture = _publicTrophyService.getLeaderboardFilterOptions();
  }

  void _reloadFilterOptions() {
    setState(() {
      _filterOptionsFuture = _publicTrophyService.getLeaderboardFilterOptions();
    });
  }

  void _resetFilters() {
    setState(() {
      _selectedCategory = _allCategoriesValue;
      _selectedSpecies = _allSpeciesValue;
      _selectedFirearmType = _allFirearmTypesValue;
      _selectionVersion++;
    });
  }

  LeaderboardCategoryOption? _findCategory(LeaderboardFilterOptions options) {
    return options.categories
        .where((candidate) => candidate.id == _selectedCategory)
        .firstOrNull;
  }

  void _applyFilters(LeaderboardFilterOptions options) {
    String? categoryId;
    String? classId;
    String? legacySpecies;
    var legacyOnly = false;
    var categoryLabel = 'Alle trofeë';
    var speciesLabel = 'Alle spesies';

    if (_selectedCategory == _legacyCategoryValue) {
      legacyOnly = true;
      categoryLabel = 'Ongekategoriseer';
      speciesLabel = _selectedSpecies == _allSpeciesValue
          ? 'Alle ongekategoriseerde spesies'
          : _selectedSpecies;
      legacySpecies = _selectedSpecies == _allSpeciesValue
          ? null
          : _selectedSpecies;
    } else if (_selectedCategory != _allCategoriesValue) {
      final category = _findCategory(options);
      if (category == null) {
        // The option disappeared after a reload; start over cleanly.
        _resetFilters();
        return;
      }
      categoryId = category.id;
      categoryLabel = category.displayNameAf;

      if (_selectedSpecies == _allSpeciesValue) {
        speciesLabel = 'Alle ${category.displayNameAf.toLowerCase()}';
      } else {
        final species = category.species
            .where((candidate) => candidate.classId == _selectedSpecies)
            .firstOrNull;
        if (species == null) {
          setState(() {
            _selectedSpecies = _allSpeciesValue;
            _selectionVersion++;
          });
          return;
        }
        classId = species.classId;
        speciesLabel = species.displayNameAf;
      }
    }

    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => LeaderboardResultsScreen(
          publicTrophyApi: _publicTrophyService,
          query: LeaderboardQuery(
            categoryId: categoryId,
            classId: classId,
            legacyOnly: legacyOnly,
            legacySpecies: legacySpecies,
            firearmType: _selectedFirearmType == _allFirearmTypesValue
                ? null
                : _selectedFirearmType,
            categoryLabel: categoryLabel,
            speciesLabel: speciesLabel,
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Trofeeranglys')),
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: FutureBuilder<LeaderboardFilterOptions>(
            future: _filterOptionsFuture,
            builder: (context, snapshot) {
              return ListView(
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 36),
                children: [
                  const _FilterHeader(),
                  const SizedBox(height: 22),
                  if (snapshot.connectionState == ConnectionState.waiting)
                    const HuntingCard(
                      child: _FilterStateCard(
                        icon: Icons.hourglass_top_rounded,
                        title: 'Filteropsies word gelaai',
                        message:
                            'Wag net ’n oomblik terwyl die opsies voorberei word.',
                        showProgress: true,
                      ),
                    )
                  else if (snapshot.hasError)
                    HuntingCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const _FilterStateCard(
                            icon: Icons.cloud_off_rounded,
                            title: 'Kon nie laai nie',
                            message: 'Kon nie filteropsies laai nie.',
                            accentColor: AppTheme.dangerSoft,
                          ),
                          const SizedBox(height: 18),
                          PrimaryButton(
                            text: 'Probeer weer',
                            icon: Icons.refresh_rounded,
                            secondary: true,
                            onPressed: _reloadFilterOptions,
                          ),
                        ],
                      ),
                    )
                  else
                    _buildFilterExperience(snapshot.data!),
                ],
              );
            },
          ),
        ),
      ),
    );
  }

  Widget _buildFilterExperience(LeaderboardFilterOptions options) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _FilterSection(
          number: '01',
          icon: Icons.pets_outlined,
          title: 'Kategorie',
          subtitle: 'Kies ’n wildkategorie vir die ranglys',
          child: DropdownMenu<String>(
            key: ValueKey('category-$_selectionVersion'),
            initialSelection: _selectedCategory,
            enableFilter: true,
            enableSearch: true,
            requestFocusOnTap: true,
            expandedInsets: EdgeInsets.zero,
            label: const Text('Gekose kategorie'),
            leadingIcon: const Icon(Icons.category_outlined),
            dropdownMenuEntries: [
              const DropdownMenuEntry(
                value: _allCategoriesValue,
                label: 'Alle trofeë',
              ),
              ...options.categories.map(
                (category) => DropdownMenuEntry(
                  value: category.id,
                  label: category.displayNameAf,
                ),
              ),
              const DropdownMenuEntry(
                value: _legacyCategoryValue,
                label: 'Ongekategoriseer',
              ),
            ],
            onSelected: (value) {
              if (value == null) {
                return;
              }
              setState(() {
                _selectedCategory = value;
                _selectedSpecies = _allSpeciesValue;
                _selectionVersion++;
              });
            },
          ),
        ),
        if (_selectedCategory != _allCategoriesValue) ...[
          const SizedBox(height: 14),
          _FilterSection(
            number: '02',
            icon: Icons.pets_outlined,
            title: 'Spesie',
            subtitle: 'Tik of kies ’n spesie binne die gekose kategorie',
            child: DropdownMenu<String>(
              key: ValueKey('species-$_selectedCategory-$_selectionVersion'),
              initialSelection: _allSpeciesValue,
              enableFilter: true,
              enableSearch: true,
              requestFocusOnTap: true,
              expandedInsets: EdgeInsets.zero,
              label: const Text('Gekose spesie'),
              leadingIcon: const Icon(Icons.search_rounded),
              dropdownMenuEntries: [
                DropdownMenuEntry(
                  value: _allSpeciesValue,
                  label: _allSpeciesLabel(options),
                ),
                ..._speciesEntries(options),
              ],
              onSelected: (value) {
                if (value == null) {
                  return;
                }
                setState(() {
                  _selectedSpecies = value;
                });
              },
            ),
          ),
        ],
        const SizedBox(height: 14),
        _FilterSection(
          number: _selectedCategory == _allCategoriesValue ? '02' : '03',
          icon: Icons.gps_fixed_rounded,
          title: 'Vuurwapentipe',
          subtitle: 'Verfyn volgens bestaande vuurwapentipe',
          child: DropdownButtonFormField<String>(
            key: ValueKey('firearm-$_selectionVersion'),
            initialValue: _selectedFirearmType,
            isExpanded: true,
            decoration: const InputDecoration(
              labelText: 'Gekose vuurwapentipe',
              prefixIcon: Icon(Icons.gps_fixed_rounded),
            ),
            items: [
              const DropdownMenuItem(
                value: _allFirearmTypesValue,
                child: Text('Alle vuurwapentipes'),
              ),
              ...options.firearmTypes.map(
                (firearmType) => DropdownMenuItem(
                  value: firearmType,
                  child: Text(firearmType, overflow: TextOverflow.ellipsis),
                ),
              ),
            ],
            onChanged: (value) {
              if (value == null) {
                return;
              }
              setState(() {
                _selectedFirearmType = value;
              });
            },
          ),
        ),
        const SizedBox(height: 24),
        const Text(
          'RANGLYS',
          style: TextStyle(
            color: AppTheme.gold,
            fontSize: 10,
            fontWeight: FontWeight.w900,
            letterSpacing: 1,
          ),
        ),
        const SizedBox(height: 9),
        PrimaryButton(
          text: 'Pas filters toe',
          icon: Icons.tune_rounded,
          onPressed: () => _applyFilters(options),
        ),
        const SizedBox(height: 10),
        TextButton.icon(
          onPressed: _resetFilters,
          icon: const Icon(Icons.restart_alt_rounded, size: 18),
          label: const Text('Stel filters terug'),
        ),
        const SizedBox(height: 2),
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Kanselleer'),
        ),
      ],
    );
  }

  String _allSpeciesLabel(LeaderboardFilterOptions options) {
    if (_selectedCategory == _legacyCategoryValue) {
      return 'Alle ongekategoriseerde spesies';
    }

    final category = _findCategory(options);
    return category == null
        ? 'Alle spesies'
        : 'Alle ${category.displayNameAf.toLowerCase()}';
  }

  List<DropdownMenuEntry<String>> _speciesEntries(
    LeaderboardFilterOptions options,
  ) {
    if (_selectedCategory == _legacyCategoryValue) {
      return options.legacySpecies
          .map((species) => DropdownMenuEntry(value: species, label: species))
          .toList();
    }

    final category = _findCategory(options);
    if (category == null) return const [];
    return category.species
        .map(
          (species) => DropdownMenuEntry(
            value: species.classId,
            label: species.displayNameAf,
          ),
        )
        .toList();
  }
}

class _FilterHeader extends StatelessWidget {
  const _FilterHeader();

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          width: 50,
          height: 50,
          decoration: BoxDecoration(
            color: AppTheme.gold.withValues(alpha: 0.13),
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
            border: Border.all(color: AppTheme.gold.withValues(alpha: 0.34)),
          ),
          child: const Icon(
            Icons.tune_rounded,
            color: AppTheme.goldSoft,
            size: 25,
          ),
        ),
        const SizedBox(width: 14),
        const Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'TROFEERANGLYS',
                style: TextStyle(
                  color: AppTheme.gold,
                  fontSize: 9,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 0.9,
                ),
              ),
              SizedBox(height: 4),
              Text(
                'Verfyn jou ranglys',
                style: TextStyle(
                  color: AppTheme.ink,
                  fontSize: 23,
                  fontWeight: FontWeight.w900,
                  height: 1.1,
                ),
              ),
              SizedBox(height: 5),
              Text(
                'Kies uit die bestaande openbare trofeefilters.',
                style: TextStyle(
                  color: AppTheme.muted,
                  fontSize: 12,
                  height: 1.35,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _FilterSection extends StatelessWidget {
  const _FilterSection({
    required this.number,
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.child,
  });

  final String number;
  final IconData icon;
  final String title;
  final String subtitle;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Container(
                width: 42,
                height: 42,
                decoration: BoxDecoration(
                  color: AppTheme.olive.withValues(alpha: 0.18),
                  borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
                  border: Border.all(
                    color: AppTheme.olive.withValues(alpha: 0.42),
                  ),
                ),
                child: Icon(icon, color: AppTheme.goldSoft, size: 21),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '$number  $title'.toUpperCase(),
                      style: const TextStyle(
                        color: AppTheme.ink,
                        fontSize: 13,
                        fontWeight: FontWeight.w900,
                        letterSpacing: 0.3,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      subtitle,
                      style: const TextStyle(
                        color: AppTheme.muted,
                        fontSize: 11.5,
                        height: 1.3,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          Divider(color: AppTheme.border.withValues(alpha: 0.75)),
          const SizedBox(height: 16),
          child,
        ],
      ),
    );
  }
}

class _FilterStateCard extends StatelessWidget {
  const _FilterStateCard({
    required this.icon,
    required this.title,
    required this.message,
    this.accentColor = AppTheme.goldSoft,
    this.showProgress = false,
  });

  final IconData icon;
  final String title;
  final String message;
  final Color accentColor;
  final bool showProgress;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          width: 48,
          height: 48,
          decoration: BoxDecoration(
            color: accentColor.withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
          ),
          child: showProgress
              ? Padding(
                  padding: const EdgeInsets.all(13),
                  child: CircularProgressIndicator(
                    color: accentColor,
                    strokeWidth: 2.3,
                  ),
                )
              : Icon(icon, color: accentColor, size: 25),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: const TextStyle(
                  color: AppTheme.ink,
                  fontSize: 18,
                  fontWeight: FontWeight.w900,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                message,
                style: const TextStyle(
                  color: AppTheme.muted,
                  fontWeight: FontWeight.w700,
                  height: 1.4,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
