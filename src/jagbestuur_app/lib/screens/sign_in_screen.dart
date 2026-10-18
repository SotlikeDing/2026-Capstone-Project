import 'package:flutter/material.dart';

import '../services/auth_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/app_logo.dart';
import '../widgets/hunting_card.dart';
import '../widgets/primary_button.dart';
import 'register_screen.dart';

class SignInScreen extends StatefulWidget {
  const SignInScreen({super.key});

  @override
  State<SignInScreen> createState() => _SignInScreenState();
}

class _SignInScreenState extends State<SignInScreen> {
  final _formKey = GlobalKey<FormState>();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();

  bool _rememberMe = true;
  bool _isLoading = false;
  bool _obscurePassword = true;

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _signIn() async {
    if (!_formKey.currentState!.validate() || _isLoading) {
      return;
    }

    setState(() {
      _isLoading = true;
    });

    try {
      await AuthService.instance.signIn(
        email: _emailController.text.trim(),
        password: _passwordController.text,
        rememberMe: _rememberMe,
      );
    } catch (e) {
      _showSignInError(e);
    } finally {
      if (mounted) {
        setState(() {
          _isLoading = false;
        });
      }
    }
  }

  void _showSignInError(Object error) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          AppErrorMessage.from(error, fallback: 'Kon nie inteken nie.'),
        ),
      ),
    );
  }

  void _openRegister() {
    Navigator.of(
      context,
    ).push(MaterialPageRoute(builder: (_) => const RegisterScreen()));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
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
                      child: Form(
                        key: _formKey,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            const Text(
                              'JOU REKENING',
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
                              'Welkom terug',
                              textAlign: TextAlign.center,
                              style: Theme.of(context).textTheme.headlineSmall,
                            ),
                            const SizedBox(height: 8),
                            const Text(
                              'Teken in om met jou jagbeplanning voort te gaan.',
                              textAlign: TextAlign.center,
                              style: TextStyle(
                                color: AppTheme.muted,
                                height: 1.45,
                              ),
                            ),
                            const SizedBox(height: 24),
                            TextFormField(
                              controller: _emailController,
                              keyboardType: TextInputType.emailAddress,
                              autofillHints: const [AutofillHints.username],
                              decoration: const InputDecoration(
                                labelText: 'E-pos',
                                prefixIcon: Icon(Icons.email_outlined),
                              ),
                              validator: (value) {
                                if (value == null || value.trim().isEmpty) {
                                  return 'Vul jou e-pos in.';
                                }
                                return null;
                              },
                            ),
                            const SizedBox(height: 14),
                            TextFormField(
                              controller: _passwordController,
                              obscureText: _obscurePassword,
                              autofillHints: const [AutofillHints.password],
                              onFieldSubmitted: (_) => _signIn(),
                              decoration: InputDecoration(
                                labelText: 'Wagwoord',
                                prefixIcon: const Icon(Icons.lock_outline),
                                suffixIcon: IconButton(
                                  onPressed: () {
                                    setState(() {
                                      _obscurePassword = !_obscurePassword;
                                    });
                                  },
                                  icon: Icon(
                                    _obscurePassword
                                        ? Icons.visibility_off_outlined
                                        : Icons.visibility_outlined,
                                  ),
                                ),
                              ),
                              validator: (value) {
                                if (value == null || value.isEmpty) {
                                  return 'Vul jou wagwoord in.';
                                }
                                return null;
                              },
                            ),
                            const SizedBox(height: 10),
                            CheckboxListTile(
                              value: _rememberMe,
                              contentPadding: EdgeInsets.zero,
                              controlAffinity: ListTileControlAffinity.leading,
                              title: const Text(
                                'Onthou my',
                                style: TextStyle(
                                  color: AppTheme.muted,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                              onChanged: _isLoading
                                  ? null
                                  : (value) {
                                      setState(() {
                                        _rememberMe = value ?? true;
                                      });
                                    },
                            ),
                            const SizedBox(height: 10),
                            PrimaryButton(
                              text: 'Teken in',
                              icon: Icons.login_rounded,
                              isLoading: _isLoading,
                              onPressed: _signIn,
                            ),
                            const SizedBox(height: 12),
                            PrimaryButton(
                              text: 'Teken in via Web',
                              icon: Icons.language_rounded,
                              secondary: true,
                              isLoading: _isLoading,
                              onPressed: () async {
                                try {
                                  await AuthService.instance.openWebSignIn();
                                } catch (e) {
                                  if (context.mounted) {
                                    ScaffoldMessenger.of(context).showSnackBar(
                                      SnackBar(
                                        content: Text(
                                          AppErrorMessage.from(
                                            e,
                                            fallback:
                                                'Kon nie webinteken oopmaak nie.',
                                          ),
                                        ),
                                      ),
                                    );
                                  }
                                }
                              },
                            ),
                            const SizedBox(height: 10),
                            TextButton(
                              onPressed: _isLoading ? null : _openRegister,
                              child: const Text(
                                'Nog nie \'n rekening nie? Registreer',
                              ),
                            ),
                          ],
                        ),
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
