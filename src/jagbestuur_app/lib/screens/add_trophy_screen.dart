import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import '../config/app_config.dart';
import '../models/rowland_ward_measurement.dart';
import '../models/rowland_ward_species.dart';
import '../models/trophy.dart';
import '../safety/app_error_barrier.dart';
import '../services/auth_service.dart';
import '../services/hunt_media_service.dart';
import '../services/rowland_ward_preference_controller.dart';
import '../services/rowland_ward_species_service.dart';
import '../services/trophy_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../utils/measurement_format.dart';
import '../widgets/hunting_card.dart';
import '../widgets/primary_button.dart';
import '../widgets/rowland_ward_measurement_form.dart';

enum TrophyFormResult { saved, deleted }

class AddTrophyScreen extends StatefulWidget {
  const AddTrophyScreen({
    super.key,
    this.trophy,
    this.rowlandWardPreferenceController,
    this.rowlandWardSpeciesApi,
  });

  final Trophy? trophy;
  final RowlandWardPreferenceController? rowlandWardPreferenceController;
  final RowlandWardSpeciesApi? rowlandWardSpeciesApi;

  @override
  State<AddTrophyScreen> createState() => _AddTrophyScreenState();
}

class _AddTrophyScreenState extends State<AddTrophyScreen> {
  final _formKey = GlobalKey<FormState>();
  final _speciesController = TextEditingController();
  final _antlerSizeController = TextEditingController();
  final _carcassWeightController = TextEditingController();
  final _shotDistanceController = TextEditingController();
  final _firearmTypeController = TextEditingController();
  final _locationController = TextEditingController();
  final _notesController = TextEditingController();
  final TrophyService _trophyService = TrophyService();
  final ImagePicker _imagePicker = ImagePicker();
  final FocusNode _speciesFocusNode = FocusNode();

  late final RowlandWardPreferenceController _rowlandWardPreferenceController;
  late final RowlandWardSpeciesApi _rowlandWardSpeciesApi;

  Timer? _speciesSearchDebounce;
  int _speciesSearchGeneration = 0;
  RowlandWardSpecies? _selectedRowlandWardSpecies;
  List<RowlandWardSpecies> _speciesSuggestions = const [];
  String? _speciesSearchMessage;
  bool _isSearchingSpecies = false;
  bool _isResolvingSpecies = false;
  bool _isUpdatingSpeciesText = false;
  int _measurementRequestGeneration = 0;
  bool _isLoadingMeasurementRequirements = false;
  String? _measurementRequirementsError;
  String? _measurementClassId;
  RowlandWardMeasurementRequirements? _measurementRequirements;
  Map<String, RowlandWardMeasurementValue> _rowlandWardMeasurementDraft =
      const {};

  DateTime? _harvestDate;
  bool _isPublic = false;
  bool _isSaving = false;
  bool _isDeleting = false;
  String _photoPath = '';
  String? _selectedLocalPhotoPath;

  bool get _isEditing => widget.trophy != null;
  bool get _isBusy =>
      _isSaving ||
      _isDeleting ||
      _isResolvingSpecies ||
      _isLoadingMeasurementRequirements;
  bool get _hasExistingRowlandWardTrophy {
    final rowlandWard = widget.trophy?.rowlandWard;
    return rowlandWard != null &&
        rowlandWard.classId.isNotEmpty &&
        rowlandWard.displayNameAf.isNotEmpty;
  }

  bool get _canonicalSpeciesFlowEnabled {
    return _rowlandWardPreferenceController.isEnabled &&
        (!_isEditing || _hasExistingRowlandWardTrophy);
  }

  bool _isOriginalRowlandWardClass(RowlandWardSpecies species) {
    return _hasExistingRowlandWardTrophy &&
        widget.trophy?.rowlandWard?.classId == species.classId;
  }

  @override
  void initState() {
    super.initState();

    _rowlandWardPreferenceController =
        widget.rowlandWardPreferenceController ??
        RowlandWardPreferenceController.instance;
    _rowlandWardSpeciesApi =
        widget.rowlandWardSpeciesApi ?? RowlandWardSpeciesService();
    _speciesController.addListener(_onSpeciesTextChanged);
    _rowlandWardPreferenceController.addListener(
      _onRowlandWardPreferenceChanged,
    );

    final trophy = widget.trophy;
    _photoPath = trophy?.photoPath ?? '';
    unawaited(_recoverLostPhoto());
    if (trophy == null) {
      return;
    }

    final rowlandWardSpecies = trophy.rowlandWard;
    if (rowlandWardSpecies != null &&
        rowlandWardSpecies.classId.isNotEmpty &&
        rowlandWardSpecies.displayNameAf.isNotEmpty) {
      _selectedRowlandWardSpecies = rowlandWardSpecies;
      _setSpeciesText(rowlandWardSpecies.displayNameAf);
    } else {
      _setSpeciesText(trophy.species);
    }
    _antlerSizeController.text = trophy.antlerSizeCm == null
        ? ''
        : formatCentimetresAsInches(trophy.antlerSizeCm!);
    _carcassWeightController.text = _formatNumber(trophy.carcassWeightKg);
    _shotDistanceController.text = trophy.shotDistanceMeters?.toString() ?? '';
    _firearmTypeController.text = trophy.firearmType;
    _locationController.text = trophy.locationText;
    _notesController.text = trophy.notes ?? '';
    _harvestDate = trophy.harvestDate;
    _isPublic = trophy.isPublic;

    if (_hasExistingRowlandWardTrophy &&
        _rowlandWardPreferenceController.isEnabled) {
      unawaited(
        _loadMeasurementRequirements(
          rowlandWardSpecies!,
          preserveExistingValues: true,
        ),
      );
    }
  }

  @override
  void dispose() {
    _speciesSearchDebounce?.cancel();
    _speciesSearchGeneration++;
    _measurementRequestGeneration++;
    _rowlandWardPreferenceController.removeListener(
      _onRowlandWardPreferenceChanged,
    );
    _speciesController.removeListener(_onSpeciesTextChanged);
    _speciesFocusNode.dispose();
    _speciesController.dispose();
    _antlerSizeController.dispose();
    _carcassWeightController.dispose();
    _shotDistanceController.dispose();
    _firearmTypeController.dispose();
    _locationController.dispose();
    _notesController.dispose();
    super.dispose();
  }

  void _onRowlandWardPreferenceChanged() {
    if (!mounted) return;

    if (!_rowlandWardPreferenceController.isEnabled) {
      _speciesSearchDebounce?.cancel();
      _speciesSearchGeneration++;
      setState(() {
        _isSearchingSpecies = false;
        _speciesSuggestions = const [];
        _speciesSearchMessage = null;
      });
      return;
    }

    setState(() {});
    final selected = _selectedRowlandWardSpecies;
    if (_canonicalSpeciesFlowEnabled &&
        selected != null &&
        _measurementRequirements == null &&
        !_isLoadingMeasurementRequirements) {
      unawaited(
        _loadMeasurementRequirements(
          selected,
          preserveExistingValues: _isOriginalRowlandWardClass(selected),
        ),
      );
      return;
    }

    if (_canonicalSpeciesFlowEnabled &&
        selected == null &&
        _speciesController.text.trim().isNotEmpty) {
      _scheduleSpeciesSearch();
    }
  }

  void _onSpeciesTextChanged() {
    if (_isUpdatingSpeciesText) return;

    final selected = _selectedRowlandWardSpecies;
    if (selected != null &&
        _normaliseSpecies(_speciesController.text) !=
            _normaliseSpecies(selected.displayNameAf)) {
      _selectedRowlandWardSpecies = null;
      _measurementRequestGeneration++;
      setState(() {
        _measurementRequirements = null;
        _measurementRequirementsError = null;
        _isLoadingMeasurementRequirements = false;
        _rowlandWardMeasurementDraft = const {};
      });
    }

    if (!_canonicalSpeciesFlowEnabled) {
      return;
    }

    _scheduleSpeciesSearch();
  }

  void _scheduleSpeciesSearch() {
    _speciesSearchDebounce?.cancel();
    final query = _speciesController.text.trim();
    final generation = ++_speciesSearchGeneration;

    if (query.isEmpty) {
      if (mounted) {
        setState(() {
          _isSearchingSpecies = false;
          _speciesSuggestions = const [];
          _speciesSearchMessage = null;
        });
      }
      return;
    }

    _speciesSearchDebounce = Timer(const Duration(milliseconds: 300), () {
      _searchSpecies(query, generation);
    });
  }

  Future<void> _searchSpecies(String query, int generation) async {
    if (!mounted || !_canonicalSpeciesFlowEnabled) return;

    setState(() {
      _isSearchingSpecies = true;
      _speciesSearchMessage = null;
    });

    try {
      final suggestions = await _rowlandWardSpeciesApi.search(query);
      if (!mounted || generation != _speciesSearchGeneration) return;

      setState(() {
        _speciesSuggestions = suggestions;
        _speciesSearchMessage = suggestions.isEmpty
            ? 'Geen goedgekeurde wildspesie stem met jou soektog ooreen nie.'
            : null;
      });
    } catch (_) {
      if (!mounted || generation != _speciesSearchGeneration) return;

      setState(() {
        _speciesSuggestions = const [];
        _speciesSearchMessage =
            'Die wildspesies kon nie gelaai word nie. Probeer weer.';
      });
    } finally {
      if (mounted && generation == _speciesSearchGeneration) {
        setState(() {
          _isSearchingSpecies = false;
        });
      }
    }
  }

  Future<bool> _selectRowlandWardSpecies(RowlandWardSpecies species) async {
    _speciesSearchDebounce?.cancel();
    _speciesSearchGeneration++;
    final alreadyLoaded =
        _measurementRequirements?.classId == species.classId &&
        _measurementRequirementsError == null;
    setState(() {
      _selectedRowlandWardSpecies = species;
      _speciesSuggestions = const [];
      _speciesSearchMessage = null;
      _isSearchingSpecies = false;
      _setSpeciesText(species.displayNameAf);
    });
    _speciesFocusNode.unfocus();

    if (alreadyLoaded) return true;
    return _loadMeasurementRequirements(
      species,
      preserveExistingValues: _isOriginalRowlandWardClass(species),
    );
  }

  Future<bool> _loadMeasurementRequirements(
    RowlandWardSpecies species, {
    required bool preserveExistingValues,
  }) async {
    final generation = ++_measurementRequestGeneration;
    final classChanged = _measurementClassId != species.classId;
    final existingValues = preserveExistingValues
        ? {
            for (final value
                in widget.trophy?.rowlandWard?.measurements ??
                    const <RowlandWardMeasurementValue>[])
              value.fieldId: value,
          }
        : const <String, RowlandWardMeasurementValue>{};

    setState(() {
      _measurementClassId = species.classId;
      _measurementRequirements = null;
      _measurementRequirementsError = null;
      _isLoadingMeasurementRequirements = true;
      if (classChanged || preserveExistingValues) {
        _rowlandWardMeasurementDraft = existingValues;
      }
    });

    try {
      final requirements = await _rowlandWardSpeciesApi
          .getMeasurementRequirements(species.classId);
      if (!mounted || generation != _measurementRequestGeneration) {
        return false;
      }
      if (requirements.classId != species.classId) {
        throw StateError('Die verkeerde metings is ontvang.');
      }

      setState(() {
        _measurementRequirements = requirements;
      });
      return true;
    } catch (_) {
      if (!mounted || generation != _measurementRequestGeneration) {
        return false;
      }

      setState(() {
        _measurementRequirementsError =
            'Die Rowland Ward-metings kon nie gelaai word nie. Probeer weer.';
      });
      return false;
    } finally {
      if (mounted && generation == _measurementRequestGeneration) {
        setState(() {
          _isLoadingMeasurementRequirements = false;
        });
      }
    }
  }

  void _setSpeciesText(String value) {
    _isUpdatingSpeciesText = true;
    _speciesController.value = TextEditingValue(
      text: value,
      selection: TextSelection.collapsed(offset: value.length),
    );
    _isUpdatingSpeciesText = false;
  }

  String _normaliseSpecies(String value) {
    return value.trim().toLowerCase().replaceAll(RegExp(r'\s+'), ' ');
  }

  bool _selectionMatchesSpeciesText() {
    final selected = _selectedRowlandWardSpecies;
    return selected != null &&
        _normaliseSpecies(_speciesController.text) ==
            _normaliseSpecies(selected.displayNameAf);
  }

  String? _speciesValidator(String? value, {required bool rowlandWardEnabled}) {
    final requiredError = _requiredValidator(
      value,
      rowlandWardEnabled ? 'Voer ’n wildspesie in.' : 'Voer ’n spesie in.',
    );
    if (requiredError != null) return requiredError;

    if (rowlandWardEnabled && !_selectionMatchesSpeciesText()) {
      return 'Kies asseblief die korrekte wildspesie.';
    }

    return null;
  }

  Future<bool> _resolveRowlandWardSpeciesForSave() async {
    if (!_canonicalSpeciesFlowEnabled) return true;
    if (_selectionMatchesSpeciesText()) {
      final selected = _selectedRowlandWardSpecies!;
      if (_measurementRequirements?.classId == selected.classId) return true;
      return _loadMeasurementRequirements(
        selected,
        preserveExistingValues: _isOriginalRowlandWardClass(selected),
      );
    }

    final input = _speciesController.text.trim();
    if (input.isEmpty) return true;

    _speciesSearchDebounce?.cancel();
    _speciesSearchGeneration++;
    setState(() {
      _isResolvingSpecies = true;
      _isSearchingSpecies = false;
      _speciesSearchMessage = null;
    });

    try {
      final result = await _rowlandWardSpeciesApi.match(input);
      if (!mounted) return false;

      if (result.canAcceptWithoutSelection) {
        return await _selectRowlandWardSpecies(result.match!);
      }

      final suggestions = <RowlandWardSpecies>[
        if (result.match != null) result.match!,
        ...result.suggestions.where(
          (suggestion) => suggestion.classId != result.match?.classId,
        ),
      ];

      setState(() {
        _speciesSuggestions = suggestions;
        _speciesSearchMessage = result.isHighConfidenceSuggestion
            ? 'Bedoel jy hierdie wildspesie? Kies dit asseblief.'
            : (result.messageAf?.trim().isNotEmpty == true
                  ? result.messageAf!.trim()
                  : 'Kies asseblief die korrekte wildspesie.');
      });
      _speciesFocusNode.requestFocus();
      return false;
    } catch (_) {
      if (!mounted) return false;

      setState(() {
        _speciesSuggestions = const [];
        _speciesSearchMessage =
            'Die wildspesie kon nie nagegaan word nie. Probeer weer.';
      });
      return false;
    } finally {
      if (mounted) {
        setState(() {
          _isResolvingSpecies = false;
        });
      }
    }
  }

  Future<void> _selectHarvestDate() async {
    final now = DateTime.now();
    final selectedDate = await showDatePicker(
      context: context,
      initialDate: _harvestDate ?? now,
      firstDate: DateTime(1900),
      lastDate: now,
      helpText: 'Kies jagdatum',
      cancelText: 'Kanselleer',
      confirmText: 'Kies',
    );

    if (selectedDate == null || !mounted) {
      return;
    }

    setState(() {
      _harvestDate = selectedDate;
    });
  }

  Future<void> _recoverLostPhoto() async {
    try {
      final response = await _imagePicker.retrieveLostData();
      final files = response.files;
      if (!mounted || files == null || files.isEmpty) return;
      await _acceptPickedPhoto(files.first);
    } catch (error) {
      debugPrint('[AddTrophyScreen] Could not recover picked photo: $error');
    }
  }

  Future<void> _choosePhoto() async {
    if (_isBusy) return;

    final source = await showModalBottomSheet<ImageSource>(
      context: context,
      backgroundColor: AppTheme.card,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(AppTheme.radiusExtraLarge),
        ),
      ),
      builder: (sheetContext) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 18),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                leading: const Icon(Icons.photo_library_outlined),
                title: const Text('Kies uit foto’s'),
                onTap: () => Navigator.pop(sheetContext, ImageSource.gallery),
              ),
              ListTile(
                leading: const Icon(Icons.photo_camera_outlined),
                title: const Text('Neem ’n foto'),
                onTap: () => Navigator.pop(sheetContext, ImageSource.camera),
              ),
            ],
          ),
        ),
      ),
    );

    if (source == null || !mounted) return;

    try {
      final photo = await _imagePicker.pickImage(
        source: source,
        maxWidth: 2400,
        imageQuality: 88,
        requestFullMetadata: false,
      );
      if (photo != null && mounted) {
        await _acceptPickedPhoto(photo);
      }
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(error, fallback: 'Kon nie die foto kies nie.'),
          ),
        ),
      );
    }
  }

  Future<void> _acceptPickedPhoto(XFile photo) async {
    final length = await photo.length();
    if (!mounted) return;

    if (length <= 0 || length > TrophyService.maxPhotoBytes) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Die foto mag nie groter as 10 MB wees nie.'),
        ),
      );
      return;
    }

    setState(() {
      _selectedLocalPhotoPath = photo.path;
    });
  }

  void _removePhoto() {
    if (_isBusy) return;
    setState(() {
      _selectedLocalPhotoPath = null;
      _photoPath = '';
    });
  }

  Widget _buildPhotoPicker() {
    final hasPhoto =
        _selectedLocalPhotoPath != null || _photoPath.trim().isNotEmpty;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
          child: AspectRatio(
            aspectRatio: 4 / 3,
            child: DecoratedBox(
              decoration: BoxDecoration(
                color: AppTheme.surface,
                border: Border.all(color: AppTheme.border),
              ),
              child: _buildPhotoImage(),
            ),
          ),
        ),
        const SizedBox(height: 12),
        OutlinedButton.icon(
          onPressed: _isBusy ? null : _choosePhoto,
          icon: const Icon(Icons.add_a_photo_outlined),
          label: Text(hasPhoto ? 'Vervang foto' : 'Kies trofeefoto'),
        ),
        if (hasPhoto) ...[
          const SizedBox(height: 6),
          TextButton.icon(
            onPressed: _isBusy ? null : _removePhoto,
            icon: const Icon(Icons.delete_outline_rounded),
            label: const Text('Verwyder foto'),
          ),
        ],
        const SizedBox(height: 8),
        const _FieldNote(
          icon: Icons.cloud_upload_outlined,
          text: 'JPEG-, PNG- en WebP-foto’s tot 10 MB word veilig opgelaai.',
        ),
      ],
    );
  }

  Widget _buildPhotoImage() {
    final localPath = _selectedLocalPhotoPath;
    if (localPath != null) {
      return Image.file(
        File(localPath),
        fit: BoxFit.cover,
        cacheWidth: 1080,
        errorBuilder: (_, _, _) => const _PhotoPlaceholder(),
      );
    }

    final resolved = AppConfig.resolveApiUrl(_photoPath);
    final uri = Uri.tryParse(resolved);
    if (uri != null && (uri.scheme == 'http' || uri.scheme == 'https')) {
      return Image.network(
        resolved,
        headers: _photoRequestHeaders,
        fit: BoxFit.cover,
        cacheWidth: 1080,
        errorBuilder: (_, _, _) => const _PhotoPlaceholder(),
      );
    }

    return const _PhotoPlaceholder();
  }

  Map<String, String>? get _photoRequestHeaders {
    final token = AuthService.instance.accessToken;
    if (token == null || token.isEmpty) {
      return null;
    }
    return {'Authorization': 'Bearer $token'};
  }

  Future<void> _saveTrophy() async {
    if (_isBusy) {
      return;
    }

    final speciesResolved = await _resolveRowlandWardSpeciesForSave();
    if (!mounted || !speciesResolved) {
      _formKey.currentState?.validate();
      return;
    }

    if (!_formKey.currentState!.validate()) {
      return;
    }

    final rowlandWardRequest = _buildRowlandWardRequest();
    if (_canonicalSpeciesFlowEnabled && rowlandWardRequest == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Vul asseblief al die vereiste Rowland Ward-metings in.',
          ),
        ),
      );
      return;
    }

    setState(() {
      _isSaving = true;
    });

    try {
      var photoPath = _photoPath;
      final localPhotoPath = _selectedLocalPhotoPath;
      if (localPhotoPath != null) {
        photoPath = await _trophyService.uploadPhoto(localPhotoPath);
        if (!mounted) {
          // No trophy request has been sent yet, so this upload is definitely
          // unattached and safe to remove.
          await _trophyService.deleteUploadedPhoto(photoPath);
          return;
        }
        setState(() {
          _photoPath = photoPath;
          _selectedLocalPhotoPath = null;
        });
      }

      final request = TrophySaveRequest(
        species: _speciesController.text,
        photoPath: photoPath,
        antlerSizeCm: _parseAntlerSizeCentimetres(_antlerSizeController.text),
        carcassWeightKg: _parseOptionalNumber(_carcassWeightController.text),
        shotDistanceMeters: _parseOptionalWholeNumber(
          _shotDistanceController.text,
        ),
        firearmType: _firearmTypeController.text,
        harvestDate: _harvestDate!,
        locationText: _locationController.text,
        notes: _notesController.text,
        isPublic: _isPublic,
        rowlandWard: rowlandWardRequest,
      );

      if (_isEditing) {
        await _trophyService.updateTrophy(
          trophyId: widget.trophy!.id,
          request: request,
        );
      } else {
        await _trophyService.createTrophy(request);
      }

      if (!mounted) {
        return;
      }

      Navigator.of(context).pop(TrophyFormResult.saved);
    } catch (error) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(
              error,
              fallback: _isEditing
                  ? 'Die trofee kon nie bygewerk word nie.'
                  : 'Die trofee kon nie bygevoeg word nie.',
            ),
          ),
        ),
      );
    } finally {
      if (mounted) {
        setState(() {
          _isSaving = false;
        });
      }
    }
  }

  Future<void> _confirmDeleteTrophy() async {
    final trophy = widget.trophy;
    if (trophy == null || _isBusy) {
      return;
    }

    final shouldDelete = await showDialog<bool>(
      context: context,
      builder: (dialogContext) {
        return AlertDialog(
          icon: const Icon(
            Icons.warning_amber_rounded,
            color: AppTheme.dangerSoft,
          ),
          title: const Text('Verwyder trofee?'),
          content: Text(
            '“${trophy.species}” sal permanent uit jou trofeekamer verwyder '
            'word. Gekoppelde jagte en private jagfoto’s sal behoue bly en '
            'slegs van hierdie trofee ontkoppel word.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text('Kanselleer'),
            ),
            FilledButton.icon(
              onPressed: () => Navigator.of(dialogContext).pop(true),
              style: FilledButton.styleFrom(
                backgroundColor: AppTheme.danger,
                foregroundColor: AppTheme.cream,
              ),
              icon: const Icon(Icons.delete_outline_rounded),
              label: const Text('Verwyder'),
            ),
          ],
        );
      },
    );

    if (shouldDelete != true || !mounted) {
      return;
    }

    setState(() {
      _isDeleting = true;
    });

    try {
      await _trophyService.deleteTrophy(trophy.id);
      try {
        await HuntMediaService().unlinkDeletedTrophy(trophy.id);
      } catch (error, stack) {
        // The server deletion already succeeded. Keep the local hunts/photos
        // and leave any stale local relationship harmlessly in place.
        AppErrorBarrier.report('Unlink deleted trophy media', error, stack);
      }

      if (!mounted) {
        return;
      }

      Navigator.of(context).pop(TrophyFormResult.deleted);
    } catch (error) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(
              error,
              fallback: 'Die trofee kon nie verwyder word nie.',
            ),
          ),
        ),
      );
    } finally {
      if (mounted) {
        setState(() {
          _isDeleting = false;
        });
      }
    }
  }

  double? _parseOptionalNumber(String value) {
    final trimmedValue = value.trim();
    if (trimmedValue.isEmpty) {
      return null;
    }

    return double.parse(trimmedValue.replaceAll(',', '.'));
  }

  double? _parseAntlerSizeCentimetres(String value) {
    final trimmedValue = value.trim();
    if (trimmedValue.isEmpty) return null;

    final existingCentimetres = widget.trophy?.antlerSizeCm;
    if (existingCentimetres != null &&
        trimmedValue.replaceAll(',', '.') ==
            formatCentimetresAsInches(existingCentimetres)) {
      return existingCentimetres;
    }

    return parseInchesAsCentimetres(trimmedValue);
  }

  int? _parseOptionalWholeNumber(String value) {
    final trimmedValue = value.trim();
    return trimmedValue.isEmpty ? null : int.parse(trimmedValue);
  }

  String _formatNumber(double? value) {
    return value == null ? '' : formatMeasurementNumber(value);
  }

  String? _requiredValidator(String? value, String message) {
    if (value == null || value.trim().isEmpty) {
      return message;
    }

    return null;
  }

  String? _measurementValidator(String? value, String fieldName) {
    final trimmedValue = value?.trim() ?? '';
    if (trimmedValue.isEmpty) {
      return null;
    }

    final normalisedValue = trimmedValue.replaceAll(',', '.');
    final validFormat = RegExp(r'^\d+(\.\d{1,2})?$').hasMatch(normalisedValue);
    final parsedValue = double.tryParse(normalisedValue);

    if (!validFormat || parsedValue == null || !parsedValue.isFinite) {
      return '$fieldName moet ’n geldige getal met hoogstens twee desimale plekke wees.';
    }

    if (parsedValue < 0) {
      return '$fieldName kan nie negatief wees nie.';
    }

    return null;
  }

  String? _shotDistanceValidator(String? value) {
    final trimmedValue = value?.trim() ?? '';
    if (trimmedValue.isEmpty) {
      return null;
    }

    final parsedValue = int.tryParse(trimmedValue);
    if (!RegExp(r'^\d+$').hasMatch(trimmedValue) || parsedValue == null) {
      return "Skootafstand moet 'n geldige nie-negatiewe heelgetal wees.";
    }

    return null;
  }

  RowlandWardEvaluationSaveRequest? _buildRowlandWardRequest() {
    if (!_canonicalSpeciesFlowEnabled) return null;

    final selected = _selectedRowlandWardSpecies;
    final requirements = _measurementRequirements;
    if (selected == null ||
        requirements == null ||
        requirements.classId != selected.classId) {
      return null;
    }

    final missingRequiredValue = requirements.requiredFields.any(
      (field) => !_rowlandWardMeasurementDraft.containsKey(field.id),
    );
    if (missingRequiredValue) return null;

    final allowedFieldIds = requirements.fields
        .map((field) => field.id)
        .toSet();
    final measurements = <String, RowlandWardMeasurementValue>{
      for (final entry in _rowlandWardMeasurementDraft.entries)
        if (allowedFieldIds.contains(entry.key)) entry.key: entry.value,
    };
    final original = widget.trophy?.rowlandWard;

    return RowlandWardEvaluationSaveRequest(
      classId: selected.classId,
      sourceVersionId: original?.classId == selected.classId
          ? original?.sourceVersionId
          : null,
      measurements: measurements,
    );
  }

  String _formatDate(DateTime? date) {
    if (date == null) {
      return 'Kies jagdatum';
    }

    const months = [
      'Jan',
      'Feb',
      'Mrt',
      'Apr',
      'Mei',
      'Jun',
      'Jul',
      'Aug',
      'Sep',
      'Okt',
      'Nov',
      'Des',
    ];

    return '${date.day} ${months[date.month - 1]} ${date.year}';
  }

  InputDecoration _fieldDecoration({
    required String label,
    required IconData icon,
    String? hint,
    String? suffixText,
    Widget? suffixIcon,
    bool alignLabelWithHint = false,
    String? errorText,
  }) {
    return InputDecoration(
      labelText: label,
      hintText: hint,
      prefixIcon: Icon(icon),
      suffixText: suffixText,
      suffixIcon: suffixIcon,
      alignLabelWithHint: alignLabelWithHint,
      errorText: errorText,
    );
  }

  Widget _buildSpeciesField(bool rowlandWardEnabled) {
    final keepLegacyEdit = _isEditing && !_hasExistingRowlandWardTrophy;
    if (!rowlandWardEnabled || keepLegacyEdit) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextFormField(
            controller: _speciesController,
            readOnly: _hasExistingRowlandWardTrophy,
            textCapitalization: TextCapitalization.words,
            decoration: _fieldDecoration(
              label: 'Wildspesie',
              icon: Icons.pets_outlined,
              suffixIcon: _hasExistingRowlandWardTrophy
                  ? const Icon(Icons.link_rounded, color: AppTheme.goldSoft)
                  : null,
            ),
            validator: (value) =>
                _speciesValidator(value, rowlandWardEnabled: false),
          ),
          if (_hasExistingRowlandWardTrophy) ...[
            const SizedBox(height: 9),
            const _FieldNote(
              icon: Icons.info_outline_rounded,
              text:
                  'Hierdie trofee se bestaande Rowland Ward-wildspesie word behou.',
            ),
          ] else if (rowlandWardEnabled && keepLegacyEdit) ...[
            const SizedBox(height: 9),
            const _FieldNote(
              icon: Icons.history_rounded,
              text:
                  'Hierdie bestaande trofee behou sy oorspronklike wildspesie.',
            ),
          ],
        ],
      );
    }

    final selected = _selectedRowlandWardSpecies;
    final isOriginalSelection =
        selected != null && _isOriginalRowlandWardClass(selected);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextFormField(
          controller: _speciesController,
          focusNode: _speciesFocusNode,
          textCapitalization: TextCapitalization.words,
          textInputAction: TextInputAction.search,
          decoration: _fieldDecoration(
            label: 'Wildspesie',
            hint: 'Begin tik, byvoorbeeld rooibok',
            icon: Icons.pets_outlined,
            suffixIcon: _isSearchingSpecies || _isResolvingSpecies
                ? const Padding(
                    padding: EdgeInsets.all(13),
                    child: SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  )
                : selected != null
                ? const Icon(
                    Icons.check_circle_outline_rounded,
                    color: AppTheme.goldSoft,
                  )
                : const Icon(Icons.search_rounded),
          ),
          validator: (value) =>
              _speciesValidator(value, rowlandWardEnabled: true),
          onFieldSubmitted: (_) async {
            await _resolveRowlandWardSpeciesForSave();
            if (mounted) _formKey.currentState?.validate();
          },
        ),
        if (isOriginalSelection) ...[
          const SizedBox(height: 9),
          const _FieldNote(
            icon: Icons.link_rounded,
            text: 'Gekoppelde Rowland Ward-wildspesie. Tik om dit te verander.',
          ),
        ] else if (selected != null && _selectionMatchesSpeciesText()) ...[
          const SizedBox(height: 9),
          _FieldNote(
            icon: Icons.check_circle_outline_rounded,
            text: 'Gekies: ${selected.displayNameAf}',
          ),
        ],
        if (_speciesSearchMessage != null) ...[
          const SizedBox(height: 9),
          Text(
            _speciesSearchMessage!,
            style: const TextStyle(
              color: AppTheme.goldSoft,
              fontSize: 11.5,
              fontWeight: FontWeight.w700,
              height: 1.35,
            ),
          ),
        ],
        if (_speciesSuggestions.isNotEmpty) ...[
          const SizedBox(height: 9),
          _SpeciesSuggestionList(
            suggestions: _speciesSuggestions,
            onSelected: (species) {
              unawaited(_selectRowlandWardSpecies(species));
            },
          ),
        ],
      ],
    );
  }

  Widget _buildRowlandWardMeasurementSection(
    RowlandWardPreferenceState preference,
  ) {
    final selected = _selectedRowlandWardSpecies;
    if (!preference.enabled ||
        !_canonicalSpeciesFlowEnabled ||
        selected == null) {
      return const SizedBox.shrink();
    }

    final requirements = _measurementRequirements;
    final error = _measurementRequirementsError;

    return Column(
      children: [
        const SizedBox(height: 14),
        _FormSection(
          number: 'RW',
          icon: Icons.architecture_rounded,
          title: 'Rowland Ward-metings',
          subtitle: requirements == null
              ? selected.displayNameAf
              : '${requirements.displayNameAf} • Metode ${requirements.methodCode}',
          children: [
            if (_isLoadingMeasurementRequirements)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 18),
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                    SizedBox(width: 12),
                    Text('Metings word gelaai...'),
                  ],
                ),
              )
            else if (error != null) ...[
              Text(
                error,
                style: const TextStyle(
                  color: AppTheme.dangerSoft,
                  fontSize: 12,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 10),
              OutlinedButton.icon(
                onPressed: () {
                  unawaited(
                    _loadMeasurementRequirements(
                      selected,
                      preserveExistingValues: _isOriginalRowlandWardClass(
                        selected,
                      ),
                    ),
                  );
                },
                icon: const Icon(Icons.refresh_rounded),
                label: const Text('Probeer weer'),
              ),
            ] else if (requirements != null &&
                requirements.requiredFields.isNotEmpty) ...[
              _FieldNote(
                icon: Icons.straighten_rounded,
                text:
                    'Vul die vereiste metings vir ${requirements.displayNameAf} in.',
              ),
              const SizedBox(height: 14),
              RowlandWardMeasurementForm(
                key: ValueKey(requirements.classId),
                requirements: requirements,
                initialValues: _rowlandWardMeasurementDraft,
                onChanged: (values) {
                  _rowlandWardMeasurementDraft = values;
                },
              ),
            ] else
              const Text(
                'Geen vereiste Rowland Ward-metings is beskikbaar nie.',
                style: TextStyle(color: AppTheme.dangerSoft),
              ),
          ],
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(_isEditing ? 'Wysig trofee' : 'Voeg trofee by'),
      ),
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 36),
              children: [
                _TrophyFormHeader(isEditing: _isEditing),
                const SizedBox(height: 20),
                _FormSection(
                  number: '01',
                  icon: Icons.photo_camera_back_outlined,
                  title: 'Trofee-identiteit',
                  subtitle: 'Spesie en trofeefoto',
                  children: [
                    ValueListenableBuilder<RowlandWardPreferenceState>(
                      valueListenable: _rowlandWardPreferenceController,
                      builder: (context, preference, _) {
                        return _buildSpeciesField(preference.enabled);
                      },
                    ),
                    const SizedBox(height: 14),
                    _buildPhotoPicker(),
                  ],
                ),
                ValueListenableBuilder<RowlandWardPreferenceState>(
                  valueListenable: _rowlandWardPreferenceController,
                  builder: (context, preference, _) {
                    return _buildRowlandWardMeasurementSection(preference);
                  },
                ),
                const SizedBox(height: 14),
                _FormSection(
                  number: '02',
                  icon: Icons.straighten_rounded,
                  title: 'Jagbesonderhede',
                  subtitle: 'Meting, toerusting en datum',
                  children: [
                    ValueListenableBuilder<RowlandWardPreferenceState>(
                      valueListenable: _rowlandWardPreferenceController,
                      builder: (context, preference, _) {
                        final showLegacyAntlerField =
                            !preference.enabled ||
                            !_canonicalSpeciesFlowEnabled;
                        if (!showLegacyAntlerField) {
                          return const SizedBox.shrink();
                        }

                        return Column(
                          children: [
                            TextFormField(
                              controller: _antlerSizeController,
                              keyboardType:
                                  const TextInputType.numberWithOptions(
                                    decimal: true,
                                  ),
                              inputFormatters: const [
                                TwoDecimalTextInputFormatter(),
                              ],
                              decoration: _fieldDecoration(
                                label: 'Horinglengte',
                                icon: Icons.straighten_rounded,
                                suffixText: 'duim',
                              ),
                              validator: (value) =>
                                  _measurementValidator(value, 'Horinglengte'),
                            ),
                            const SizedBox(height: 14),
                          ],
                        );
                      },
                    ),
                    TextFormField(
                      controller: _carcassWeightController,
                      keyboardType: const TextInputType.numberWithOptions(
                        decimal: true,
                      ),
                      inputFormatters: const [TwoDecimalTextInputFormatter()],
                      decoration: _fieldDecoration(
                        label: 'Karkasgewig',
                        icon: Icons.scale_outlined,
                        suffixText: 'kg',
                      ),
                      validator: (value) =>
                          _measurementValidator(value, 'Karkasgewig'),
                    ),
                    const SizedBox(height: 14),
                    TextFormField(
                      controller: _shotDistanceController,
                      keyboardType: TextInputType.number,
                      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                      decoration: _fieldDecoration(
                        label: 'Skootafstand (m)',
                        icon: Icons.social_distance_rounded,
                      ),
                      validator: _shotDistanceValidator,
                    ),
                    const SizedBox(height: 14),
                    TextFormField(
                      controller: _firearmTypeController,
                      textCapitalization: TextCapitalization.words,
                      decoration: _fieldDecoration(
                        label: 'Vuurwapentipe',
                        icon: Icons.gps_fixed_rounded,
                      ),
                    ),
                    const SizedBox(height: 14),
                    FormField<DateTime>(
                      validator: (_) {
                        if (_harvestDate == null) {
                          return 'Kies ’n jagdatum.';
                        }
                        return null;
                      },
                      builder: (state) {
                        return InkWell(
                          onTap: _isBusy
                              ? null
                              : () async {
                                  await _selectHarvestDate();
                                  if (!mounted) {
                                    return;
                                  }
                                  state.didChange(_harvestDate);
                                },
                          borderRadius: BorderRadius.circular(
                            AppTheme.radiusMedium,
                          ),
                          child: InputDecorator(
                            decoration: _fieldDecoration(
                              label: 'Jagdatum',
                              icon: Icons.calendar_month_outlined,
                              suffixIcon: const Icon(Icons.expand_more_rounded),
                              errorText: state.errorText,
                            ),
                            child: Text(
                              _formatDate(_harvestDate),
                              style: TextStyle(
                                color: _harvestDate == null
                                    ? AppTheme.muted
                                    : AppTheme.ink,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                _FormSection(
                  number: '03',
                  icon: Icons.edit_note_rounded,
                  title: _isEditing ? 'Notas' : 'Plek en notas',
                  subtitle: _isEditing
                      ? 'Bykomende trofeebesonderhede'
                      : 'Bestaande ligging en bykomende besonderhede',
                  children: [
                    if (!_isEditing) ...[
                      TextFormField(
                        controller: _locationController,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: _fieldDecoration(
                          label: 'Ligging',
                          icon: Icons.location_on_outlined,
                        ),
                      ),
                      const SizedBox(height: 14),
                    ],
                    TextFormField(
                      controller: _notesController,
                      minLines: 3,
                      maxLines: 5,
                      textCapitalization: TextCapitalization.sentences,
                      decoration: _fieldDecoration(
                        label: 'Notas',
                        icon: Icons.notes_rounded,
                        alignLabelWithHint: true,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                _FormSection(
                  number: '04',
                  icon: Icons.visibility_outlined,
                  title: 'Sigbaarheid',
                  subtitle: 'Kies wie hierdie trofee kan sien',
                  children: [
                    _VisibilityChoice(
                      icon: Icons.lock_outline_rounded,
                      title: 'Privaat',
                      subtitle: 'Net jy kan hierdie trofee sien.',
                      selected: !_isPublic,
                      onTap: _isBusy
                          ? null
                          : () {
                              setState(() {
                                _isPublic = false;
                              });
                            },
                    ),
                    const SizedBox(height: 10),
                    _VisibilityChoice(
                      icon: Icons.public_rounded,
                      title: 'Publiek',
                      subtitle:
                          'Hierdie trofee kan in die trofeeranglys verskyn.',
                      selected: _isPublic,
                      onTap: _isBusy
                          ? null
                          : () {
                              setState(() {
                                _isPublic = true;
                              });
                            },
                    ),
                  ],
                ),
                const SizedBox(height: 24),
                Text(
                  _isEditing ? 'STOOR VERANDERINGE' : 'STOOR TROFEE',
                  style: const TextStyle(
                    color: AppTheme.gold,
                    fontSize: 10,
                    fontWeight: FontWeight.w900,
                    letterSpacing: 1,
                  ),
                ),
                const SizedBox(height: 9),
                PrimaryButton(
                  text: _isEditing ? 'Stoor veranderinge' : 'Stoor trofee',
                  icon: Icons.save_rounded,
                  isLoading: _isSaving || _isResolvingSpecies,
                  onPressed: _saveTrophy,
                ),
                const SizedBox(height: 10),
                OutlinedButton(
                  onPressed: _isBusy ? null : () => Navigator.of(context).pop(),
                  child: const Text('Kanselleer'),
                ),
                if (_isEditing) ...[
                  const SizedBox(height: 30),
                  const _DangerDivider(),
                  const SizedBox(height: 12),
                  OutlinedButton.icon(
                    onPressed: _isBusy ? null : _confirmDeleteTrophy,
                    style: OutlinedButton.styleFrom(
                      minimumSize: const Size(double.infinity, 52),
                      foregroundColor: AppTheme.dangerSoft,
                      side: BorderSide(
                        color: AppTheme.danger.withValues(alpha: 0.78),
                      ),
                    ),
                    icon: _isDeleting
                        ? const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(
                              color: AppTheme.dangerSoft,
                              strokeWidth: 2,
                            ),
                          )
                        : const Icon(Icons.delete_outline_rounded),
                    label: Text(
                      _isDeleting
                          ? 'Trofee word verwyder...'
                          : 'Verwyder trofee',
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _TrophyFormHeader extends StatelessWidget {
  const _TrophyFormHeader({required this.isEditing});

  final bool isEditing;

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
          child: Icon(
            isEditing
                ? Icons.edit_note_rounded
                : Icons.add_photo_alternate_outlined,
            color: AppTheme.goldSoft,
            size: 26,
          ),
        ),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'MY TROFEË',
                style: TextStyle(
                  color: AppTheme.gold,
                  fontSize: 9,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 0.9,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                isEditing ? 'Wysig trofeebesonderhede' : 'Trofeebesonderhede',
                style: const TextStyle(
                  color: AppTheme.ink,
                  fontSize: 23,
                  fontWeight: FontWeight.w900,
                  height: 1.1,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _FormSection extends StatelessWidget {
  const _FormSection({
    required this.number,
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.children,
  });

  final String number;
  final IconData icon;
  final String title;
  final String subtitle;
  final List<Widget> children;

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
          ...children,
        ],
      ),
    );
  }
}

class _FieldNote extends StatelessWidget {
  const _FieldNote({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, color: AppTheme.muted, size: 16),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            text,
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 11.5,
              height: 1.4,
            ),
          ),
        ),
      ],
    );
  }
}

class _SpeciesSuggestionList extends StatelessWidget {
  const _SpeciesSuggestionList({
    required this.suggestions,
    required this.onSelected,
  });

  final List<RowlandWardSpecies> suggestions;
  final ValueChanged<RowlandWardSpecies> onSelected;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
        border: Border.all(color: AppTheme.border),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Padding(
            padding: EdgeInsets.fromLTRB(13, 10, 13, 7),
            child: Text(
              'KIES ’N WILDSPESIE',
              style: TextStyle(
                color: AppTheme.gold,
                fontSize: 9,
                fontWeight: FontWeight.w900,
                letterSpacing: 0.8,
              ),
            ),
          ),
          for (var index = 0; index < suggestions.length; index++) ...[
            if (index > 0)
              Divider(
                height: 1,
                color: AppTheme.border.withValues(alpha: 0.65),
              ),
            InkWell(
              onTap: () => onSelected(suggestions[index]),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 13,
                  vertical: 11,
                ),
                child: Row(
                  children: [
                    const Icon(
                      Icons.pets_outlined,
                      color: AppTheme.goldSoft,
                      size: 19,
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            suggestions[index].displayNameAf,
                            style: const TextStyle(
                              color: AppTheme.ink,
                              fontSize: 13.5,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                          if (suggestions[index].latinName
                              .trim()
                              .isNotEmpty) ...[
                            const SizedBox(height: 2),
                            Text(
                              suggestions[index].latinName,
                              style: const TextStyle(
                                color: AppTheme.muted,
                                fontSize: 11,
                                fontStyle: FontStyle.italic,
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                    const Icon(
                      Icons.chevron_right_rounded,
                      color: AppTheme.muted,
                      size: 20,
                    ),
                  ],
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _PhotoPlaceholder extends StatelessWidget {
  const _PhotoPlaceholder();

  @override
  Widget build(BuildContext context) {
    return const Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            Icons.photo_camera_back_outlined,
            color: AppTheme.gold,
            size: 42,
          ),
          SizedBox(height: 8),
          Text(
            'Geen trofeefoto gekies nie',
            style: TextStyle(
              color: AppTheme.muted,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

class _VisibilityChoice extends StatelessWidget {
  const _VisibilityChoice({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.selected,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final bool selected;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final accent = selected ? AppTheme.goldSoft : AppTheme.muted;

    return Semantics(
      button: true,
      selected: selected,
      enabled: onTap != null,
      child: Opacity(
        opacity: onTap == null ? 0.56 : 1,
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            onTap: onTap,
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
            child: Ink(
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                color: selected
                    ? AppTheme.olive.withValues(alpha: 0.18)
                    : AppTheme.surface,
                borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
                border: Border.all(
                  color: selected
                      ? AppTheme.gold.withValues(alpha: 0.62)
                      : AppTheme.border,
                  width: selected ? 1.4 : 1,
                ),
              ),
              child: Row(
                children: [
                  Container(
                    width: 40,
                    height: 40,
                    decoration: BoxDecoration(
                      color: accent.withValues(alpha: 0.1),
                      borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
                    ),
                    child: Icon(icon, color: accent, size: 20),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          title,
                          style: TextStyle(
                            color: selected ? AppTheme.ink : AppTheme.muted,
                            fontSize: 14,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        const SizedBox(height: 3),
                        Text(
                          subtitle,
                          style: const TextStyle(
                            color: AppTheme.muted,
                            fontSize: 11,
                            height: 1.3,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 10),
                  Container(
                    width: 22,
                    height: 22,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      border: Border.all(
                        color: selected ? AppTheme.goldSoft : AppTheme.border,
                        width: 1.5,
                      ),
                    ),
                    alignment: Alignment.center,
                    child: selected
                        ? Container(
                            width: 10,
                            height: 10,
                            decoration: const BoxDecoration(
                              color: AppTheme.goldSoft,
                              shape: BoxShape.circle,
                            ),
                          )
                        : null,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _DangerDivider extends StatelessWidget {
  const _DangerDivider();

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: Divider(color: AppTheme.danger.withValues(alpha: 0.45)),
        ),
        const Padding(
          padding: EdgeInsets.symmetric(horizontal: 12),
          child: Text(
            'VERWYDER TROFEE',
            style: TextStyle(
              color: AppTheme.dangerSoft,
              fontSize: 9,
              fontWeight: FontWeight.w900,
              letterSpacing: 0.9,
            ),
          ),
        ),
        Expanded(
          child: Divider(color: AppTheme.danger.withValues(alpha: 0.45)),
        ),
      ],
    );
  }
}
