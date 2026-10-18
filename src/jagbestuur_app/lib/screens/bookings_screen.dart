import 'dart:async';

import 'package:flutter/material.dart';

import '../models/booking.dart';
import '../models/app_session.dart';
import '../models/chat_models.dart';
import '../screens/chat_screen.dart';
import '../services/booking_service.dart';
import '../services/chat_service.dart';
import '../services/farm_rating_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/app_logo.dart';
import '../widgets/booking_hunters_sheet.dart';
import '../widgets/hunting_card.dart';
import '../widgets/new_chat_dialog.dart';
import 'booking_workspace_screen.dart';
import 'team_rating_screen.dart';

class BookingsScreen extends StatefulWidget {
  const BookingsScreen({
    super.key,
    required this.userRoleId,
    required this.currentUserId,
    required this.session,
    this.activation = 0,
  });

  final int userRoleId;
  final int currentUserId;
  final AppSession session;

  /// Changes each time the tab is reopened; triggers a reload.
  final int activation;

  @override
  State<BookingsScreen> createState() => _BookingsScreenState();
}

class _BookingsScreenState extends State<BookingsScreen> {
  final BookingService _bookingService = BookingService();
  final ChatService _chatService = ChatService();
  final FarmRatingService _farmRatingService = FarmRatingService();
  final Set<int> _openingBookingChats = <int>{};

  late Future<List<Booking>> _bookingsFuture;
  bool _showPastBookings = false;

  @override
  void initState() {
    super.initState();
    _bookingsFuture = _bookingService.getBookings(
      userRoleId: widget.userRoleId,
    );
  }

  @override
  void didUpdateWidget(covariant BookingsScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.activation != widget.activation) {
      unawaited(_refreshBookings());
    }
  }

  Future<void> _contactFarm(Booking booking) async {
    final ownerId = booking.farmOwnerUserId;
    if (ownerId == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Hierdie bespreking is nie aan ’n boer gekoppel nie. Kontak die '
            'plaas deur die webwerf.',
          ),
        ),
      );
      return;
    }

    // Protect against a quick double-tap creating two identical chats while
    // the first network request is still running.
    if (!_openingBookingChats.add(booking.id)) return;

    try {
      final existing = await _chatService.findDirectChat(
        currentUserId: widget.currentUserId,
        otherUserId: ownerId,
      );
      if (!mounted) return;

      if (existing != null) {
        await Navigator.of(context).push(
          MaterialPageRoute(
            builder: (_) => ChatScreen(
              chatId: existing.id,
              title: existing.displayTitle,
              currentUserId: widget.currentUserId,
            ),
          ),
        );
        return;
      }

      await startNewChat(
        context,
        currentUserId: widget.currentUserId,
        recipient: ChatContact(id: ownerId, displayName: booking.farmName),
        initialTitle: 'Gesprek oor ${booking.title}',
        chatService: _chatService,
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(
              error,
              fallback: 'Kon nie die bestaande gesprek nagaan nie.',
            ),
          ),
        ),
      );
    } finally {
      _openingBookingChats.remove(booking.id);
    }
  }

  Future<void> _showFarmRatingDialog(Booking booking) async {
    var selectedStars = booking.myFarmRatingStars ?? 0;
    var saving = false;
    final descriptionController = TextEditingController(
      text: booking.myFarmRatingDescription ?? '',
    );

    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.card,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(AppTheme.radiusExtraLarge),
        ),
      ),
      builder: (sheetContext) {
        return StatefulBuilder(
          builder: (context, setModalState) {
            return SafeArea(
              top: false,
              child: Padding(
                padding: EdgeInsets.fromLTRB(
                  20,
                  12,
                  20,
                  24 + MediaQuery.viewInsetsOf(context).bottom,
                ),
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Center(
                        child: Container(
                          width: 42,
                          height: 4,
                          decoration: BoxDecoration(
                            color: AppTheme.border,
                            borderRadius: BorderRadius.circular(99),
                          ),
                        ),
                      ),
                      const SizedBox(height: 18),
                      const Text(
                        'PLAASGRADERING',
                        style: TextStyle(
                          color: AppTheme.gold,
                          fontSize: 10,
                          fontWeight: FontWeight.w900,
                          letterSpacing: 1,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        booking.farmName,
                        style: const TextStyle(
                          color: AppTheme.ink,
                          fontSize: 24,
                          fontWeight: FontWeight.w900,
                        ),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        booking.myFarmRatingStars == null
                            ? 'Kies ’n gradering van 0 tot 5 sterre.'
                            : 'Verander jou bestaande gradering indien nodig.',
                        style: const TextStyle(
                          color: AppTheme.muted,
                          fontSize: 13,
                          height: 1.4,
                        ),
                      ),
                      const SizedBox(height: 20),
                      Center(
                        child: Text(
                          '$selectedStars / 5',
                          style: const TextStyle(
                            color: AppTheme.goldSoft,
                            fontSize: 24,
                            fontWeight: FontWeight.w900,
                          ),
                        ),
                      ),
                      const SizedBox(height: 8),
                      Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          ChoiceChip(
                            label: const Text('0'),
                            selected: selectedStars == 0,
                            onSelected: saving
                                ? null
                                : (_) => setModalState(() => selectedStars = 0),
                          ),
                          const SizedBox(width: 5),
                          for (var star = 1; star <= 5; star++)
                            IconButton(
                              tooltip: '$star ster${star == 1 ? '' : 're'}',
                              onPressed: saving
                                  ? null
                                  : () => setModalState(
                                      () => selectedStars = star,
                                    ),
                              icon: Icon(
                                star <= selectedStars
                                    ? Icons.star_rounded
                                    : Icons.star_border_rounded,
                                color: AppTheme.goldSoft,
                                size: 34,
                              ),
                              padding: const EdgeInsets.all(2),
                              constraints: const BoxConstraints(
                                minWidth: 40,
                                minHeight: 44,
                              ),
                            ),
                        ],
                      ),
                      const SizedBox(height: 18),
                      TextField(
                        controller: descriptionController,
                        enabled: !saving,
                        minLines: 3,
                        maxLines: 5,
                        maxLength: 2000,
                        decoration: const InputDecoration(
                          labelText: 'Beskrywing (opsioneel)',
                          hintText: 'Deel kortliks jou ervaring op die plaas…',
                          alignLabelWithHint: true,
                        ),
                      ),
                      const SizedBox(height: 12),
                      Row(
                        children: [
                          Expanded(
                            child: OutlinedButton(
                              onPressed: saving
                                  ? null
                                  : () => Navigator.pop(sheetContext, false),
                              child: const Text('Kanselleer'),
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: ElevatedButton.icon(
                              onPressed: saving
                                  ? null
                                  : () async {
                                      setModalState(() => saving = true);
                                      try {
                                        await _farmRatingService.saveRating(
                                          bookingId: booking.id,
                                          stars: selectedStars,
                                          description:
                                              descriptionController.text,
                                        );
                                        if (sheetContext.mounted) {
                                          Navigator.pop(sheetContext, true);
                                        }
                                      } catch (error) {
                                        if (!sheetContext.mounted) return;
                                        setModalState(() => saving = false);
                                        ScaffoldMessenger.of(
                                          sheetContext,
                                        ).showSnackBar(
                                          SnackBar(
                                            content: Text(
                                              AppErrorMessage.from(
                                                error,
                                                fallback:
                                                    'Kon nie die plaasgradering stoor nie.',
                                              ),
                                            ),
                                          ),
                                        );
                                      }
                                    },
                              icon: saving
                                  ? const SizedBox(
                                      width: 18,
                                      height: 18,
                                      child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                      ),
                                    )
                                  : const Icon(Icons.star_rounded),
                              label: Text(
                                booking.myFarmRatingStars == null
                                    ? 'Stoor'
                                    : 'Werk by',
                              ),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            );
          },
        );
      },
    );

    descriptionController.dispose();
    if (saved != true || !mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Plaasgradering suksesvol gestoor.')),
    );
    await _refreshBookings();
  }

  void _showBookingDetails(Booking booking) {
    showModalBottomSheet<void>(
      context: context,
      backgroundColor: AppTheme.card,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(AppTheme.radiusExtraLarge),
        ),
      ),
      builder: (sheetContext) {
        return SafeArea(
          top: false,
          child: SingleChildScrollView(
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Center(
                  child: Container(
                    width: 42,
                    height: 4,
                    decoration: BoxDecoration(
                      color: AppTheme.border,
                      borderRadius: BorderRadius.circular(99),
                    ),
                  ),
                ),
                const SizedBox(height: 18),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Expanded(
                      child: Text(
                        'BESPREKING',
                        style: TextStyle(
                          color: AppTheme.gold,
                          fontSize: 10,
                          fontWeight: FontWeight.w900,
                          letterSpacing: 1,
                        ),
                      ),
                    ),
                    _BookingStatusBadge(status: booking.status),
                  ],
                ),
                const SizedBox(height: 10),
                Text(
                  booking.title,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 25,
                    fontWeight: FontWeight.w900,
                    height: 1.15,
                    letterSpacing: -0.3,
                  ),
                ),
                const SizedBox(height: 18),
                _BookingDatePanel(booking: booking),
                if (!booking.isPast && booking.hasFarmReviews) ...[
                  const SizedBox(height: 14),
                  _FarmRatingSummary(booking: booking),
                ],
                const SizedBox(height: 14),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: AppTheme.surface,
                    borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
                    border: Border.all(color: AppTheme.border),
                  ),
                  child: Column(
                    children: [
                      _DetailRow(
                        icon: Icons.cabin_rounded,
                        text: booking.farmName,
                      ),
                      _DetailRow(
                        icon: Icons.location_on_rounded,
                        text: booking.location,
                      ),
                      _DetailRow(
                        icon: booking.isMainHunter
                            ? Icons.person_rounded
                            : Icons.handshake_rounded,
                        text: booking.isMainHunter
                            ? booking.hunterName
                            : 'Saam met ${booking.hunterName}',
                      ),
                      _DetailRow(
                        icon: Icons.groups_rounded,
                        text:
                            '${booking.hunterCount} / ${booking.maxHunters} jagters • ${booking.nonHuntGuests} nie-jaggaste',
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 20),
                if (booking.canRateFarm) ...[
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton.icon(
                      onPressed: () {
                        Navigator.pop(sheetContext);
                        unawaited(_showFarmRatingDialog(booking));
                      },
                      icon: const Icon(Icons.star_rounded),
                      label: Text(
                        booking.myFarmRatingStars == null
                            ? 'Gradeer hierdie plaas'
                            : 'Verander plaasgradering',
                      ),
                    ),
                  ),
                  const SizedBox(height: 10),
                ],
                if (widget.session.isHunter &&
                    (booking.maxHunters > 1 || !booking.isMainHunter)) ...[
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton.icon(
                      onPressed: () {
                        Navigator.pop(sheetContext);
                        unawaited(_openHunters(booking));
                      },
                      icon: const Icon(Icons.groups_rounded),
                      label: Text(
                        booking.canManageHunters
                            ? 'Bestuur jagters'
                            : 'Jagters',
                      ),
                    ),
                  ),
                  const SizedBox(height: 10),
                ],
                if (booking.isPast &&
                    widget.session.can(
                      MobileCapabilities.viewBookingMedia,
                    )) ...[
                  SizedBox(
                    width: double.infinity,
                    child: FilledButton.icon(
                      onPressed: () {
                        Navigator.pop(sheetContext);
                        unawaited(
                          Navigator.of(context).push<void>(
                            MaterialPageRoute(
                              builder: (_) => BookingWorkspaceScreen(
                                booking: booking,
                                session: widget.session,
                              ),
                            ),
                          ),
                        );
                      },
                      icon: const Icon(Icons.photo_library_outlined),
                      label: const Text('Gedeelde jagmedia en terugblik'),
                    ),
                  ),
                  const SizedBox(height: 10),
                ],
                // Added hunters (companions) aren't rating participants - only the
                // main hunter, the PH and the VG rate each other.
                if (booking.isPast &&
                    (!widget.session.isHunter || booking.isMainHunter)) ...[
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton.icon(
                      onPressed: () {
                        Navigator.pop(sheetContext);
                        unawaited(
                          Navigator.of(context).push<void>(
                            MaterialPageRoute(
                              builder: (_) =>
                                  TeamRatingScreen(booking: booking),
                            ),
                          ),
                        );
                      },
                      icon: const Icon(Icons.reviews_outlined),
                      label: const Text('Gradeer die span'),
                    ),
                  ),
                  const SizedBox(height: 10),
                ],
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton.icon(
                    onPressed: () {
                      Navigator.pop(sheetContext);
                      unawaited(_contactFarm(booking));
                    },
                    icon: const Icon(Icons.message_rounded),
                    label: const Text('Kontak PH / Boer'),
                  ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  /// The hunters on the booking; the list reloads afterwards because the main
  /// hunter may have changed the roster or this hunter may have left the trip.
  Future<void> _openHunters(Booking booking) async {
    await BookingHuntersSheet.show(context, booking, service: _bookingService);
    await _refreshBookings();
  }

  /// Errors are shown by the FutureBuilder; they are not rethrown to the
  /// RefreshIndicator.
  Future<void> _refreshBookings() async {
    if (!mounted) return;
    setState(() {
      _bookingsFuture = _bookingService.getBookings(
        userRoleId: widget.userRoleId,
      );
    });

    try {
      await _bookingsFuture;
    } catch (_) {}
  }

  Widget _buildHeader({int? count}) {
    final countLabel = _showPastBookings ? 'vorige' : 'opkomende';

    return Column(
      children: [
        const AppLogo(compact: true, showTagline: false),
        const SizedBox(height: 20),
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            const Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'JOU JAGKALENDER',
                    style: TextStyle(
                      color: AppTheme.gold,
                      fontSize: 10,
                      fontWeight: FontWeight.w900,
                      letterSpacing: 1,
                    ),
                  ),
                  SizedBox(height: 6),
                  Text(
                    'Besprekings',
                    style: TextStyle(
                      color: AppTheme.ink,
                      fontSize: 28,
                      fontWeight: FontWeight.w900,
                      letterSpacing: -0.4,
                    ),
                  ),
                ],
              ),
            ),
            if (count != null)
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 11,
                  vertical: 7,
                ),
                decoration: BoxDecoration(
                  color: AppTheme.olive.withValues(alpha: 0.18),
                  borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
                  border: Border.all(
                    color: AppTheme.olive.withValues(alpha: 0.42),
                  ),
                ),
                child: Text(
                  '$count $countLabel',
                  style: const TextStyle(
                    color: AppTheme.goldSoft,
                    fontSize: 11,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ),
          ],
        ),
        const SizedBox(height: 8),
        Align(
          alignment: Alignment.centerLeft,
          child: Text(
            _showPastBookings
                ? 'Kyk terug na jagte wat reeds afgehandel is.'
                : 'Hou jou volgende jagte, datums en status op een plek.',
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 14,
              height: 1.4,
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildStateCard({
    required IconData icon,
    required String title,
    required String message,
    Color accentColor = AppTheme.gold,
    bool showProgress = false,
  }) {
    return HuntingCard(
      padding: const EdgeInsets.all(20),
      child: Row(
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
                      strokeWidth: 2.4,
                    ),
                  )
                : Icon(icon, color: accentColor, size: 25),
          ),
          const SizedBox(width: 14),
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
                const SizedBox(height: 6),
                Text(
                  message,
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 13,
                    height: 1.4,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSectionHeader() {
    return Row(
      children: [
        Container(
          width: 42,
          height: 42,
          decoration: BoxDecoration(
            color: (_showPastBookings ? AppTheme.muted : AppTheme.olive)
                .withValues(alpha: 0.14),
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
          ),
          child: Icon(
            _showPastBookings
                ? Icons.history_rounded
                : Icons.event_available_rounded,
            color: _showPastBookings ? AppTheme.muted : AppTheme.goldSoft,
            size: 22,
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                _showPastBookings ? 'JAGGESKIEDENIS' : 'VOLGENDE JAGTE',
                style: const TextStyle(
                  color: AppTheme.gold,
                  fontSize: 9,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 0.9,
                ),
              ),
              const SizedBox(height: 3),
              Text(
                _showPastBookings ? 'Vorige besprekings' : 'Opkomende jagte',
                style: const TextStyle(
                  color: AppTheme.ink,
                  fontSize: 18,
                  fontWeight: FontWeight.w900,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(width: 10),
        OutlinedButton.icon(
          onPressed: () {
            setState(() {
              _showPastBookings = !_showPastBookings;
            });
          },
          icon: Icon(
            _showPastBookings
                ? Icons.event_available_rounded
                : Icons.history_rounded,
            size: 18,
          ),
          label: Text(_showPastBookings ? 'Opkomend' : 'Vorige'),
          style: OutlinedButton.styleFrom(
            minimumSize: const Size(0, 44),
            padding: const EdgeInsets.symmetric(horizontal: 12),
          ),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: RefreshIndicator(
            onRefresh: _refreshBookings,
            child: FutureBuilder<List<Booking>>(
              future: _bookingsFuture,
              builder: (context, snapshot) {
                final isLoading =
                    snapshot.connectionState == ConnectionState.waiting;
                final bookings = snapshot.data ?? const <Booking>[];

                final upcomingBookings = bookings
                    .where((booking) => !booking.isPast)
                    .toList();

                final pastBookings = bookings
                    .where((booking) => booking.isPast)
                    .toList();

                final visibleBookings = _showPastBookings
                    ? pastBookings
                    : upcomingBookings;

                return ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 12, 16, 120),
                  children: [
                    _buildHeader(
                      count: isLoading || snapshot.hasError
                          ? null
                          : visibleBookings.length,
                    ),
                    const SizedBox(height: 28),
                    if (!isLoading && !snapshot.hasError) ...[
                      _buildSectionHeader(),
                      const SizedBox(height: 14),
                    ],
                    if (isLoading)
                      _buildStateCard(
                        icon: Icons.hourglass_top_rounded,
                        title: 'Besprekings word gelaai',
                        message:
                            'Wag net ’n oomblik terwyl jou jagkalender voorberei word.',
                        accentColor: AppTheme.goldSoft,
                        showProgress: true,
                      )
                    else if (snapshot.hasError)
                      _buildStateCard(
                        icon: Icons.cloud_off_rounded,
                        title: 'Kon nie besprekings laai nie',
                        message:
                            'Trek die skerm af om weer met die diens te probeer koppel.',
                        accentColor: AppTheme.dangerSoft,
                      )
                    else if (visibleBookings.isEmpty)
                      _buildStateCard(
                        icon: _showPastBookings
                            ? Icons.history_toggle_off_rounded
                            : Icons.event_busy_rounded,
                        title: _showPastBookings
                            ? 'Geen vorige besprekings nie'
                            : 'Geen opkomende besprekings nie',
                        message: _showPastBookings
                            ? 'Afgehandelde jagte sal hier verskyn.'
                            : 'Jou volgende jagbespreking sal hier verskyn.',
                        accentColor: _showPastBookings
                            ? AppTheme.muted
                            : AppTheme.goldSoft,
                      )
                    else
                      for (
                        var index = 0;
                        index < visibleBookings.length;
                        index++
                      ) ...[
                        _BookingCard(
                          booking: visibleBookings[index],
                          onTap: () =>
                              _showBookingDetails(visibleBookings[index]),
                        ),
                        if (index != visibleBookings.length - 1)
                          const SizedBox(height: 12),
                      ],
                  ],
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}

class _BookingCard extends StatelessWidget {
  const _BookingCard({required this.booking, required this.onTap});

  final Booking booking;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      onTap: onTap,
      padding: EdgeInsets.zero,
      backgroundColor: booking.isPast ? AppTheme.surface : AppTheme.card,
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(15, 16, 15, 14),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _DateStamp(date: booking.startDate, isPast: booking.isPast),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      _BookingStatusBadge(status: booking.status),
                      const SizedBox(height: 10),
                      Text(
                        booking.title,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          color: AppTheme.ink,
                          fontSize: 19,
                          fontWeight: FontWeight.w900,
                          height: 1.15,
                        ),
                      ),
                      const SizedBox(height: 10),
                      _CardMetadataRow(
                        icon: Icons.cabin_rounded,
                        text: booking.farmName,
                      ),
                      const SizedBox(height: 6),
                      _CardMetadataRow(
                        icon: Icons.location_on_rounded,
                        text: booking.location,
                      ),
                      if (!booking.isMainHunter) ...[
                        const SizedBox(height: 6),
                        _CardMetadataRow(
                          icon: Icons.handshake_rounded,
                          text: 'Saam met ${booking.hunterName}',
                        ),
                      ],
                      if (!booking.isPast && booking.hasFarmReviews) ...[
                        const SizedBox(height: 9),
                        _FarmRatingInline(booking: booking),
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),
          const Divider(height: 1),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 15, vertical: 11),
            child: Row(
              children: [
                const Icon(
                  Icons.calendar_month_rounded,
                  color: AppTheme.gold,
                  size: 18,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    booking.date,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: AppTheme.goldSoft,
                      fontSize: 12,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                const Text(
                  'Besonderhede',
                  style: TextStyle(
                    color: AppTheme.muted,
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(width: 3),
                const Icon(
                  Icons.chevron_right_rounded,
                  color: AppTheme.muted,
                  size: 19,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _DateStamp extends StatelessWidget {
  const _DateStamp({required this.date, required this.isPast});

  final DateTime date;
  final bool isPast;

  static const _months = [
    'JAN',
    'FEB',
    'MRT',
    'APR',
    'MEI',
    'JUN',
    'JUL',
    'AUG',
    'SEP',
    'OKT',
    'NOV',
    'DES',
  ];

  @override
  Widget build(BuildContext context) {
    final accentColor = isPast ? AppTheme.muted : AppTheme.goldSoft;

    return Container(
      width: 62,
      padding: const EdgeInsets.symmetric(vertical: 11),
      decoration: BoxDecoration(
        color: (isPast ? AppTheme.cardLight : AppTheme.olive).withValues(
          alpha: isPast ? 0.52 : 0.18,
        ),
        borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
        border: Border.all(
          color: (isPast ? AppTheme.border : AppTheme.olive).withValues(
            alpha: 0.68,
          ),
        ),
      ),
      child: Column(
        children: [
          Text(
            _months[date.month - 1],
            style: TextStyle(
              color: accentColor,
              fontSize: 10,
              fontWeight: FontWeight.w900,
              letterSpacing: 0.8,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            '${date.day}',
            style: const TextStyle(
              color: AppTheme.ink,
              fontSize: 25,
              fontWeight: FontWeight.w900,
              height: 1,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            '${date.year}',
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 10,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

class _BookingStatusBadge extends StatelessWidget {
  const _BookingStatusBadge({required this.status});

  final String status;

  Color get _statusColor {
    switch (status) {
      case 'Goedgekeur':
      case 'Betaal': // paid = confirmed, not waiting
        return AppTheme.olive;
      case 'Voltooi':
        return AppTheme.gold;
      case 'Afgekeur':
        return AppTheme.dangerSoft;
      case 'Gekanselleer':
        return AppTheme.muted;
      case 'Wag Nog':
      default:
        return AppTheme.goldSoft;
    }
  }

  IconData get _statusIcon {
    switch (status) {
      case 'Goedgekeur':
        return Icons.check_circle_rounded;
      case 'Betaal':
        return Icons.payments_rounded;
      case 'Voltooi':
        return Icons.task_alt_rounded;
      case 'Afgekeur':
        return Icons.cancel_rounded;
      case 'Gekanselleer':
        return Icons.do_not_disturb_on_rounded;
      case 'Wag Nog':
      default:
        return Icons.hourglass_top_rounded;
    }
  }

  @override
  Widget build(BuildContext context) {
    final color = _statusColor;
    final approved = status == 'Goedgekeur' || status == 'Betaal';

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(
        color: color.withValues(alpha: approved ? 0.34 : 0.12),
        borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
        border: Border.all(color: color.withValues(alpha: 0.52)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(_statusIcon, color: approved ? AppTheme.cream : color, size: 14),
          const SizedBox(width: 5),
          Text(
            status,
            style: TextStyle(
              color: approved ? AppTheme.cream : color,
              fontSize: 10,
              fontWeight: FontWeight.w900,
            ),
          ),
        ],
      ),
    );
  }
}

class _BookingDatePanel extends StatelessWidget {
  const _BookingDatePanel({required this.booking});

  final Booking booking;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(15),
      decoration: BoxDecoration(
        gradient: const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF30351F), Color(0xFF252019)],
        ),
        borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
        border: Border.all(color: AppTheme.olive.withValues(alpha: 0.42)),
      ),
      child: Row(
        children: [
          const _ContainerIcon(
            icon: Icons.calendar_month_rounded,
            color: AppTheme.goldSoft,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'JAGDATUMS',
                  style: TextStyle(
                    color: AppTheme.gold,
                    fontSize: 9,
                    fontWeight: FontWeight.w900,
                    letterSpacing: 0.8,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  booking.date,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 16,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ContainerIcon extends StatelessWidget {
  const _ContainerIcon({required this.icon, required this.color});

  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 42,
      height: 42,
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
      ),
      child: Icon(icon, color: color, size: 22),
    );
  }
}

class _CardMetadataRow extends StatelessWidget {
  const _CardMetadataRow({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Icon(icon, color: AppTheme.muted, size: 16),
        const SizedBox(width: 7),
        Expanded(
          child: Text(
            text,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 12,
              height: 1.2,
            ),
          ),
        ),
      ],
    );
  }
}

class _FarmRatingInline extends StatelessWidget {
  const _FarmRatingInline({required this.booking});

  final Booking booking;

  @override
  Widget build(BuildContext context) {
    final reviewLabel = booking.farmReviewCount == 1
        ? '1 resensie'
        : '${booking.farmReviewCount} resensies';

    return Row(
      children: [
        const Icon(Icons.star_rounded, color: AppTheme.goldSoft, size: 18),
        const SizedBox(width: 6),
        Text(
          '${booking.farmAverageRating!.toStringAsFixed(1)} / 5',
          style: const TextStyle(
            color: AppTheme.goldSoft,
            fontSize: 12,
            fontWeight: FontWeight.w900,
          ),
        ),
        const SizedBox(width: 7),
        Expanded(
          child: Text(
            '• $reviewLabel',
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 11,
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
      ],
    );
  }
}

class _FarmRatingSummary extends StatelessWidget {
  const _FarmRatingSummary({required this.booking});

  final Booking booking;

  @override
  Widget build(BuildContext context) {
    final reviewLabel = booking.farmReviewCount == 1
        ? 'Gebaseer op 1 resensie'
        : 'Gebaseer op ${booking.farmReviewCount} resensies';

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(15),
      decoration: BoxDecoration(
        color: AppTheme.gold.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
        border: Border.all(color: AppTheme.gold.withValues(alpha: 0.32)),
      ),
      child: Row(
        children: [
          const _ContainerIcon(
            icon: Icons.star_rounded,
            color: AppTheme.goldSoft,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'PLAASGRADERING',
                  style: TextStyle(
                    color: AppTheme.gold,
                    fontSize: 9,
                    fontWeight: FontWeight.w900,
                    letterSpacing: 0.8,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  '${booking.farmAverageRating!.toStringAsFixed(1)} / 5',
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 18,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  reviewLabel,
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(
              color: AppTheme.gold.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(9),
            ),
            child: Icon(icon, color: AppTheme.goldSoft, size: 17),
          ),
          const SizedBox(width: 11),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Text(
                text,
                style: const TextStyle(
                  color: AppTheme.ink,
                  fontSize: 13,
                  fontWeight: FontWeight.w700,
                  height: 1.35,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
