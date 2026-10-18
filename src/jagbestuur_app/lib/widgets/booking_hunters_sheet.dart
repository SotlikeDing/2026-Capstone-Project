import 'package:flutter/material.dart';

import '../models/booking.dart';
import '../models/booking_hunter.dart';
import '../services/booking_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';

/// The hunters on a booking. The main hunter adds hunters (by name + email, up
/// to the booking's hunter count, once it is confirmed) or removes them; an
/// added hunter sees who is going and can leave the trip. The server enforces
/// every rule - this sheet only shows what it allows. Callers reload their
/// bookings list when it closes (an added hunter may have left the trip).
class BookingHuntersSheet extends StatefulWidget {
  const BookingHuntersSheet({super.key, required this.booking, this.service});

  final Booking booking;
  final BookingService? service;

  static Future<void> show(
    BuildContext context,
    Booking booking, {
    BookingService? service,
  }) {
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.card,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(AppTheme.radiusExtraLarge),
        ),
      ),
      builder: (_) => BookingHuntersSheet(booking: booking, service: service),
    );
  }

  @override
  State<BookingHuntersSheet> createState() => _BookingHuntersSheetState();
}

class _BookingHuntersSheetState extends State<BookingHuntersSheet> {
  late final BookingService _service = widget.service ?? BookingService();
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _emailController = TextEditingController();

  late Future<BookingHunters> _future = _service.getHunters(widget.booking.id);
  bool _busy = false;
  String? _message;
  bool _messageIsError = false;

  @override
  void dispose() {
    _nameController.dispose();
    _emailController.dispose();
    super.dispose();
  }

  void _reload() {
    setState(() {
      _future = _service.getHunters(widget.booking.id);
    });
  }

  Future<void> _add() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      final message = await _service.addHunter(
        widget.booking.id,
        name: _nameController.text.trim(),
        email: _emailController.text.trim(),
      );
      _nameController.clear();
      _emailController.clear();
      _show(message, error: false);
      _reload();
    } catch (error) {
      _show(
        AppErrorMessage.from(error, fallback: 'Kon nie die jagter byvoeg nie.'),
        error: true,
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _remove(BookingHunter hunter) async {
    final leaving = hunter.isYou;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(
          leaving ? 'Verlaat hierdie jag?' : 'Verwyder ${hunter.name}?',
        ),
        content: Text(
          leaving
              ? 'Jy sal nie meer die besonderhede van hierdie bespreking sien nie. Die hoofjagter kan jou weer byvoeg.'
              : 'Hulle sal nie meer die besonderhede van hierdie bespreking sien nie.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Kanselleer'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: Text(leaving ? 'Verlaat' : 'Verwyder'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busy = true);
    try {
      await _service.removeHunter(widget.booking.id, hunter.companionId);
      if (leaving) {
        if (mounted) Navigator.pop(context);
        return;
      }
      _show('${hunter.name} is verwyder.', error: false);
      _reload();
    } catch (error) {
      _show(
        AppErrorMessage.from(
          error,
          fallback: 'Kon nie die jagter verwyder nie.',
        ),
        error: true,
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _show(String message, {required bool error}) {
    if (!mounted) return;
    setState(() {
      _message = message;
      _messageIsError = error;
    });
  }

  @override
  Widget build(BuildContext context) {
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
          child: FutureBuilder<BookingHunters>(
            future: _future,
            builder: (context, snapshot) {
              final roster = snapshot.data;
              return Column(
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
                    children: [
                      const Expanded(
                        child: Text(
                          'JAGTERS',
                          style: TextStyle(
                            color: AppTheme.gold,
                            fontSize: 10,
                            fontWeight: FontWeight.w900,
                            letterSpacing: 1,
                          ),
                        ),
                      ),
                      if (roster != null)
                        Text(
                          '${roster.hunters.length} / ${roster.maxHunters}',
                          style: const TextStyle(
                            color: AppTheme.muted,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(
                    widget.booking.title,
                    style: const TextStyle(
                      color: AppTheme.ink,
                      fontSize: 20,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                  const SizedBox(height: 14),
                  if (snapshot.connectionState != ConnectionState.done)
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 24),
                      child: Center(child: CircularProgressIndicator()),
                    )
                  else if (snapshot.hasError || roster == null)
                    Text(
                      AppErrorMessage.from(
                        snapshot.error,
                        fallback: 'Kon nie die jagters laai nie.',
                      ),
                      style: const TextStyle(color: AppTheme.dangerSoft),
                    )
                  else ...[
                    for (final hunter in roster.hunters)
                      _hunterTile(roster, hunter),
                    const SizedBox(height: 12),
                    ..._addSection(roster),
                  ],
                  if (_message != null) ...[
                    const SizedBox(height: 12),
                    Text(
                      _message!,
                      style: TextStyle(
                        color: _messageIsError
                            ? AppTheme.dangerSoft
                            : AppTheme.goldSoft,
                      ),
                    ),
                  ],
                ],
              );
            },
          ),
        ),
      ),
    );
  }

  Widget _hunterTile(BookingHunters roster, BookingHunter hunter) {
    final String label;
    final Color color;
    if (hunter.isMainHunter) {
      label = 'Hoofjagter';
      color = AppTheme.gold;
    } else if (hunter.isPending) {
      label = 'Uitgenooi';
      color = AppTheme.muted;
    } else {
      label = 'Gekoppel';
      color = AppTheme.olive;
    }
    final canRemove =
        !hunter.isMainHunter && (roster.canManage || hunter.isYou);

    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      decoration: BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
        border: Border.all(color: AppTheme.border),
      ),
      child: Row(
        children: [
          Icon(
            hunter.isMainHunter ? Icons.star_rounded : Icons.person_rounded,
            color: color,
            size: 20,
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  hunter.isYou ? '${hunter.name} (jy)' : hunter.name,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontWeight: FontWeight.w800,
                  ),
                ),
                if (hunter.email != null)
                  Text(
                    hunter.email!,
                    style: const TextStyle(color: AppTheme.muted, fontSize: 12),
                  ),
                Text(
                  label,
                  style: TextStyle(
                    color: color,
                    fontSize: 11,
                    fontWeight: FontWeight.w800,
                  ),
                ),
              ],
            ),
          ),
          if (canRemove)
            IconButton(
              tooltip: hunter.isYou ? 'Verlaat jag' : 'Verwyder',
              onPressed: _busy ? null : () => _remove(hunter),
              icon: Icon(
                hunter.isYou
                    ? Icons.logout_rounded
                    : Icons.person_remove_rounded,
                color: AppTheme.dangerSoft,
              ),
            ),
        ],
      ),
    );
  }

  List<Widget> _addSection(BookingHunters roster) {
    if (!roster.canManage) {
      if (roster.callerIsMainHunter && roster.maxHunters > 1) {
        return const [
          Text(
            'Jy kan die ander jagters byvoeg sodra die bespreking bevestig is.',
            style: TextStyle(color: AppTheme.muted),
          ),
        ];
      }
      return const [];
    }
    if (roster.openSlots == 0) {
      return const [
        Text(
          'Al die jagterplekke op hierdie bespreking is gevul.',
          style: TextStyle(color: AppTheme.muted),
        ),
      ];
    }
    return [
      Text(
        'Voeg ’n jagter by (nog ${roster.openSlots} plek${roster.openSlots == 1 ? '' : 'ke'} oop). '
        'Slegs jagters, nie waarnemers nie. Wie nog nie ’n rekening het nie, kry ’n uitnodiging per e-pos.',
        style: const TextStyle(color: AppTheme.muted, fontSize: 13),
      ),
      const SizedBox(height: 10),
      Form(
        key: _formKey,
        child: Column(
          children: [
            TextFormField(
              controller: _nameController,
              textCapitalization: TextCapitalization.words,
              maxLength: 200,
              decoration: const InputDecoration(
                labelText: 'Naam en van',
                counterText: '',
              ),
              validator: (v) =>
                  (v == null || v.trim().isEmpty) ? 'Voer die naam in.' : null,
            ),
            const SizedBox(height: 8),
            TextFormField(
              controller: _emailController,
              keyboardType: TextInputType.emailAddress,
              autocorrect: false,
              maxLength: 255,
              decoration: const InputDecoration(
                labelText: 'E-posadres',
                counterText: '',
              ),
              validator: (v) {
                final value = v?.trim() ?? '';
                return RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(value)
                    ? null
                    : 'Voer ’n geldige e-posadres in.';
              },
            ),
            const SizedBox(height: 12),
            SizedBox(
              width: double.infinity,
              child: ElevatedButton.icon(
                onPressed: _busy ? null : _add,
                icon: const Icon(Icons.person_add_alt_1_rounded),
                label: const Text('Voeg jagter by'),
              ),
            ),
          ],
        ),
      ),
    ];
  }
}
