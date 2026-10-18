import 'package:flutter/material.dart';

import '../services/auth_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/app_logo.dart';
import '../widgets/hunting_card.dart';
import '../widgets/primary_button.dart';

class RegisterScreen extends StatelessWidget {
  const RegisterScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Registreer')),
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(16),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 420),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const AppLogo(compact: true, showTagline: false),
                    const SizedBox(height: 28),
                    HuntingCard(
                      padding: const EdgeInsets.all(22),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          const Icon(
                            Icons.person_add_alt_1_rounded,
                            color: AppTheme.goldSoft,
                            size: 42,
                          ),
                          const SizedBox(height: 16),
                          const Text(
                            'NUWE REKENING',
                            textAlign: TextAlign.center,
                            style: TextStyle(
                              color: AppTheme.goldSoft,
                              fontSize: 11,
                              fontWeight: FontWeight.w800,
                              letterSpacing: 1.2,
                            ),
                          ),
                          const SizedBox(height: 8),
                          Text(
                            'Registreer u rekening',
                            textAlign: TextAlign.center,
                            style: Theme.of(context).textTheme.headlineSmall,
                          ),
                          const SizedBox(height: 10),
                          const Text(
                            'Registreer gerieflik op u webblaaier. Na voltooiing sal u outomaties herlei word na die app met u rekening ingeteken.',
                            textAlign: TextAlign.center,
                            style: TextStyle(color: AppTheme.muted, height: 1.5),
                          ),
                          const SizedBox(height: 24),
                          PrimaryButton(
                            text: 'Registreer via Web',
                            icon: Icons.language_rounded,
                            onPressed: () async {
                              try {
                                await AuthService.instance.openWebRegister();
                              } catch (e) {
                                if (context.mounted) {
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(
                                        AppErrorMessage.from(
                                          e,
                                          fallback:
                                              'Kon nie webregistrasie oopmaak nie.',
                                        ),
                                      ),
                                    ),
                                  );
                                }
                              }
                            },
                          ),
                          const SizedBox(height: 12),
                          OutlinedButton.icon(
                            onPressed: () => Navigator.of(context).pop(),
                            icon: const Icon(Icons.arrow_back_rounded),
                            label: const Text('Terug na inteken'),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
