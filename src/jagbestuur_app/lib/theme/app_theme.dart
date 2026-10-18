import 'package:flutter/material.dart';

class AppTheme {
  // Core bushveld palette.
  static const Color cream = Color(0xFFF3EBDD);
  static const Color olive = Color(0xFF58633A);

  static const Color background = Color(0xFF14120F);
  static const Color surface = Color(0xFF1B1814);
  static const Color card = Color(0xFF231E19);
  static const Color cardLight = Color(0xFF302A23);
  static const Color border = Color(0xFF443C32);
  static const Color muted = Color(0xFFA79D8E);
  static const Color ink = Color(0xFFF4EEE4);

  static const Color gold = Color(0xFFB58A50);
  static const Color goldSoft = Color(0xFFD1B17D);
  static const Color green = olive;
  static const Color danger = Color(0xFFA65340);
  static const Color dangerSoft = Color(0xFFE2A091);

  static const Color disabledSurface = Color(0xFF29251F);
  static const Color disabledText = Color(0xFF746D63);

  // Shared shape scale for centrally themed Material components.
  static const double radiusSmall = 10;
  static const double radiusMedium = 14;
  static const double radiusLarge = 18;
  static const double radiusExtraLarge = 24;

  static const BoxDecoration pageDecoration = BoxDecoration(
    gradient: LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [Color(0xFF15130F), Color(0xFF211B15)],
    ),
  );

  static const List<BoxShadow> cardShadow = [
    BoxShadow(color: Color(0x52000000), blurRadius: 18, offset: Offset(0, 8)),
    BoxShadow(color: Color(0x0AFFFFFF), blurRadius: 1, offset: Offset(0, 1)),
  ];

  static const TextTheme _textTheme = TextTheme(
    displaySmall: TextStyle(
      color: ink,
      fontSize: 36,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.6,
      height: 1.08,
    ),
    headlineMedium: TextStyle(
      color: ink,
      fontSize: 28,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.3,
      height: 1.12,
    ),
    headlineSmall: TextStyle(
      color: ink,
      fontSize: 24,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.2,
      height: 1.16,
    ),
    titleLarge: TextStyle(
      color: ink,
      fontSize: 20,
      fontWeight: FontWeight.w700,
      height: 1.2,
    ),
    titleMedium: TextStyle(
      color: ink,
      fontSize: 16,
      fontWeight: FontWeight.w700,
      height: 1.3,
    ),
    titleSmall: TextStyle(
      color: ink,
      fontSize: 14,
      fontWeight: FontWeight.w700,
      letterSpacing: 0.1,
      height: 1.3,
    ),
    bodyLarge: TextStyle(
      color: ink,
      fontSize: 16,
      fontWeight: FontWeight.w400,
      height: 1.5,
    ),
    bodyMedium: TextStyle(
      color: ink,
      fontSize: 14,
      fontWeight: FontWeight.w400,
      height: 1.45,
    ),
    bodySmall: TextStyle(
      color: muted,
      fontSize: 12,
      fontWeight: FontWeight.w400,
      height: 1.4,
    ),
    labelLarge: TextStyle(
      color: ink,
      fontSize: 15,
      fontWeight: FontWeight.w700,
      letterSpacing: 0.2,
      height: 1.2,
    ),
    labelMedium: TextStyle(
      color: muted,
      fontSize: 12,
      fontWeight: FontWeight.w700,
      letterSpacing: 0.3,
      height: 1.2,
    ),
    labelSmall: TextStyle(
      color: muted,
      fontSize: 11,
      fontWeight: FontWeight.w600,
      letterSpacing: 0.3,
      height: 1.2,
    ),
  );

  static ThemeData get darkHuntingTheme {
    final colorScheme =
        ColorScheme.fromSeed(
          seedColor: olive,
          brightness: Brightness.dark,
        ).copyWith(
          primary: olive,
          onPrimary: cream,
          primaryContainer: const Color(0xFF303720),
          onPrimaryContainer: const Color(0xFFDDE5C2),
          secondary: gold,
          onSecondary: background,
          secondaryContainer: const Color(0xFF3B2D1D),
          onSecondaryContainer: const Color(0xFFF0D5AA),
          surface: surface,
          onSurface: ink,
          surfaceContainerLowest: background,
          surfaceContainerLow: surface,
          surfaceContainer: card,
          surfaceContainerHigh: cardLight,
          surfaceContainerHighest: const Color(0xFF3A332B),
          error: danger,
          onError: cream,
          errorContainer: const Color(0xFF3B211B),
          onErrorContainer: const Color(0xFFF0B5A8),
          outline: border,
          outlineVariant: const Color(0xFF342E27),
          shadow: Colors.black,
          scrim: Colors.black,
          inverseSurface: cream,
          onInverseSurface: background,
          inversePrimary: const Color(0xFF768252),
        );

    const primaryButtonTextStyle = TextStyle(
      fontSize: 15,
      fontWeight: FontWeight.w800,
      letterSpacing: 0.2,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      scaffoldBackgroundColor: background,
      canvasColor: background,
      cardColor: card,
      disabledColor: disabledText,
      fontFamily: 'Roboto',
      colorScheme: colorScheme,
      textTheme: _textTheme,
      primaryTextTheme: _textTheme,
      dividerColor: border,
      splashColor: const Color(0x1FB58A50),
      highlightColor: const Color(0x12B58A50),
      focusColor: const Color(0x2EB58A50),
      hoverColor: const Color(0x14B58A50),
      iconTheme: const IconThemeData(color: muted, size: 22),
      primaryIconTheme: const IconThemeData(color: cream, size: 22),
      progressIndicatorTheme: const ProgressIndicatorThemeData(
        color: gold,
        linearTrackColor: cardLight,
        circularTrackColor: cardLight,
      ),
      dividerTheme: const DividerThemeData(
        color: border,
        thickness: 1,
        space: 1,
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: background,
        foregroundColor: ink,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        surfaceTintColor: Colors.transparent,
        iconTheme: IconThemeData(color: muted),
        actionsIconTheme: IconThemeData(color: muted),
        titleTextStyle: TextStyle(
          color: ink,
          fontSize: 23,
          fontWeight: FontWeight.w800,
          letterSpacing: -0.2,
          height: 1.15,
        ),
      ),
      cardTheme: CardThemeData(
        color: card,
        surfaceTintColor: Colors.transparent,
        shadowColor: Colors.black,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(radiusLarge),
          side: const BorderSide(color: border),
        ),
      ),
      bottomNavigationBarTheme: const BottomNavigationBarThemeData(
        backgroundColor: card,
        selectedItemColor: goldSoft,
        unselectedItemColor: muted,
        type: BottomNavigationBarType.fixed,
        elevation: 0,
        selectedLabelStyle: TextStyle(
          fontWeight: FontWeight.w700,
          fontSize: 11,
          letterSpacing: 0.1,
        ),
        unselectedLabelStyle: TextStyle(
          fontWeight: FontWeight.w600,
          fontSize: 11,
        ),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: card,
        elevation: 0,
        height: 72,
        indicatorColor: olive.withValues(alpha: 0.32),
        iconTheme: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return const IconThemeData(color: goldSoft);
          }
          return const IconThemeData(color: muted);
        }),
        labelTextStyle: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return const TextStyle(
              color: ink,
              fontSize: 11,
              fontWeight: FontWeight.w700,
            );
          }
          return const TextStyle(
            color: muted,
            fontSize: 11,
            fontWeight: FontWeight.w600,
          );
        }),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: surface,
        labelStyle: const TextStyle(color: muted, fontWeight: FontWeight.w600),
        floatingLabelStyle: const TextStyle(
          color: goldSoft,
          fontWeight: FontWeight.w700,
        ),
        hintStyle: const TextStyle(color: disabledText),
        helperStyle: const TextStyle(color: muted, height: 1.35),
        errorStyle: const TextStyle(
          color: dangerSoft,
          fontWeight: FontWeight.w600,
          height: 1.3,
        ),
        prefixIconColor: muted,
        suffixIconColor: muted,
        iconColor: muted,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 16,
        ),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
          borderSide: const BorderSide(color: border),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
          borderSide: const BorderSide(color: border),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
          borderSide: const BorderSide(color: gold, width: 1.6),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
          borderSide: const BorderSide(color: danger),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
          borderSide: const BorderSide(color: dangerSoft, width: 1.6),
        ),
        disabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
          borderSide: const BorderSide(color: Color(0xFF342F29)),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: olive,
          foregroundColor: cream,
          disabledBackgroundColor: disabledSurface,
          disabledForegroundColor: disabledText,
          elevation: 0,
          shadowColor: Colors.transparent,
          minimumSize: const Size(64, 54),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 15),
          textStyle: primaryButtonTextStyle,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(radiusMedium),
          ),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: olive,
          foregroundColor: cream,
          disabledBackgroundColor: disabledSurface,
          disabledForegroundColor: disabledText,
          minimumSize: const Size(64, 54),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 15),
          textStyle: primaryButtonTextStyle,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(radiusMedium),
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: goldSoft,
          disabledForegroundColor: disabledText,
          minimumSize: const Size(64, 52),
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 14),
          textStyle: primaryButtonTextStyle,
          side: const BorderSide(color: border),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(radiusMedium),
          ),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: goldSoft,
          disabledForegroundColor: disabledText,
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
          textStyle: const TextStyle(
            fontSize: 14,
            fontWeight: FontWeight.w700,
            letterSpacing: 0.1,
          ),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(radiusSmall),
          ),
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          foregroundColor: muted,
          disabledForegroundColor: disabledText,
          highlightColor: const Color(0x1FB58A50),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(radiusMedium),
          ),
        ),
      ),
      checkboxTheme: CheckboxThemeData(
        checkColor: const WidgetStatePropertyAll(cream),
        fillColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.disabled)) {
            return disabledSurface;
          }
          if (states.contains(WidgetState.selected)) {
            return olive;
          }
          return Colors.transparent;
        }),
        side: const BorderSide(color: muted, width: 1.4),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(5)),
      ),
      radioTheme: RadioThemeData(
        fillColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.disabled)) {
            return disabledText;
          }
          if (states.contains(WidgetState.selected)) {
            return goldSoft;
          }
          return muted;
        }),
      ),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.disabled)) {
            return disabledText;
          }
          if (states.contains(WidgetState.selected)) {
            return cream;
          }
          return muted;
        }),
        trackColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.disabled)) {
            return disabledSurface;
          }
          if (states.contains(WidgetState.selected)) {
            return olive;
          }
          return cardLight;
        }),
        trackOutlineColor: const WidgetStatePropertyAll(border),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: surface,
        selectedColor: olive,
        disabledColor: disabledSurface,
        checkmarkColor: cream,
        labelStyle: const TextStyle(
          color: ink,
          fontSize: 12,
          fontWeight: FontWeight.w700,
        ),
        secondaryLabelStyle: const TextStyle(
          color: cream,
          fontSize: 12,
          fontWeight: FontWeight.w700,
        ),
        side: const BorderSide(color: border),
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
        ),
      ),
      listTileTheme: const ListTileThemeData(
        iconColor: muted,
        textColor: ink,
        selectedColor: goldSoft,
        selectedTileColor: Color(0xFF2B281D),
      ),
      snackBarTheme: SnackBarThemeData(
        backgroundColor: cardLight,
        actionTextColor: goldSoft,
        disabledActionTextColor: disabledText,
        closeIconColor: muted,
        contentTextStyle: const TextStyle(
          color: ink,
          fontSize: 14,
          fontWeight: FontWeight.w600,
          height: 1.35,
        ),
        elevation: 10,
        behavior: SnackBarBehavior.floating,
        insetPadding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(radiusMedium),
          side: const BorderSide(color: border),
        ),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: card,
        surfaceTintColor: Colors.transparent,
        elevation: 12,
        shadowColor: Colors.black,
        insetPadding: const EdgeInsets.symmetric(horizontal: 24, vertical: 24),
        actionsPadding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        titleTextStyle: const TextStyle(
          color: ink,
          fontSize: 22,
          fontWeight: FontWeight.w800,
          letterSpacing: -0.2,
          height: 1.2,
        ),
        contentTextStyle: const TextStyle(
          color: muted,
          fontSize: 14,
          height: 1.5,
        ),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(radiusLarge),
          side: const BorderSide(color: border),
        ),
      ),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: card,
        modalBackgroundColor: card,
        surfaceTintColor: Colors.transparent,
        elevation: 12,
        modalElevation: 12,
        shadowColor: Colors.black,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(
            top: Radius.circular(radiusExtraLarge),
          ),
          side: BorderSide(color: border),
        ),
      ),
      datePickerTheme: DatePickerThemeData(
        backgroundColor: card,
        surfaceTintColor: Colors.transparent,
        headerBackgroundColor: surface,
        headerForegroundColor: ink,
        weekdayStyle: const TextStyle(
          color: muted,
          fontWeight: FontWeight.w700,
        ),
        dayForegroundColor: const WidgetStatePropertyAll(ink),
        todayForegroundColor: const WidgetStatePropertyAll(goldSoft),
        todayBorder: const BorderSide(color: gold),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(radiusLarge),
          side: const BorderSide(color: border),
        ),
      ),
      textSelectionTheme: const TextSelectionThemeData(
        cursorColor: goldSoft,
        selectionColor: Color(0x6658633A),
        selectionHandleColor: gold,
      ),
      tooltipTheme: TooltipThemeData(
        decoration: BoxDecoration(
          color: cardLight,
          borderRadius: BorderRadius.circular(radiusSmall),
          border: Border.all(color: border),
        ),
        textStyle: const TextStyle(
          color: ink,
          fontSize: 12,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
