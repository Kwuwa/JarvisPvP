using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace PvPTopPlates;

/// <summary>
/// Interface texts in English, French, German and Japanese, plus Russian (LocRu.cs) with the game terms kept in English.
/// Game terms come from the game itself so they match the official client text of the chosen language:
/// {GUARD} / {ELIXIR} = Action 29054 / 29055 names and {HP} / {MP} = BaseParam 7 / 8, read from the game data
/// (verified values below are the fallback); {LB}, {CC}, roles, {SIGNS}, {PVP} etc. are the official Addon texts
/// (Limit Break = Addon 1190, roles = 2784-2786, Crystalline Conflict = 17033, Signs = 7599, PvP = 4900,
/// debuffs = 5803 / 8013, party list = 7641), checked in all four languages.
/// </summary>
internal static partial class Loc
{
    public static readonly string[] LanguageNames = { "Game language", "English", "Français", "Deutsch", "日本語", "Русский" };

    private static readonly Dictionary<string, string[]> Texts = new(StringComparer.Ordinal)
    {
        // window frame: title, "Test in world", status (only Latin-1 and CJK characters: the default font has no
        // general punctuation such as en dashes, ellipses or curly quotes)
        ["window_title"] = new[] { "Jarvis PvP Settings", "Paramètres de Jarvis PvP", "Einstellungen von Jarvis PvP", "Jarvis PvP 設定" },
        ["test_in_world"] = new[] { "Test in world", "Tester en jeu", "Im Spiel testen", "ゲーム画面でテスト" },
        ["test_in_world_help"] = new[]
        {
            "Draws a sample enemy plate above your own character in the game world: {GUARD} cycling between active and recast, the {LB} gauge filling up, debuffs, a sign and the marks of an enemy that has you as its target. Use it to check sizes and positions without a match. Turns off when this window closes.",
            "Affiche un exemple de barre ennemie au-dessus de votre personnage, dans le monde du jeu : {GUARD} alternant entre actif et recharge, jauge de {LB} qui se remplit, effets néfastes, signe de ciblage et indicateurs d'un ennemi qui vous cible. Pratique pour vérifier tailles et positions sans match. Se désactive à la fermeture de cette fenêtre.",
            "Zeigt über deinem eigenen Charakter in der Spielwelt eine Beispiel-Leiste eines Gegners: {GUARD} wechselt zwischen aktiv und Reaktivierung, die {LB}-Leiste füllt sich, dazu negative Status, eine Feindmarkierung und die Hinweise auf einen Gegner, der dich anvisiert. So lassen sich Größen und Positionen ohne Match prüfen. Schaltet sich beim Schließen dieses Fensters ab.",
            "ゲーム画面の自分のキャラクターの上に、敵のサンプルバーを表示します（{GUARD}の効果中とリキャストの繰り返し、たまっていく{LB}ゲージ、デバフ、ターゲットマーカー、自分を狙う敵の表示）。試合なしでサイズや位置を確認できます。このウィンドウを閉じると自動でオフになります。",
        },
        ["st_active"] = new[] { "Active", "Actif", "Aktiv", "動作中" },
        ["st_waiting"] = new[] { "Waiting for {PVP}", "En attente du {PVP}", "Wartet auf {PVP}", "{PVP}待機中" },
        ["st_paused"] = new[] { "Turned off", "Désactivé", "Ausgeschaltet", "オフ" },
        ["status_pvp"] = new[] { "{PVP}: plates are drawn.", "{PVP} : les barres sont affichées.", "{PVP}: Leisten werden angezeigt.", "{PVP}：バーを表示中。" },
        ["status_positioning"] = new[] { "Outside {PVP}: drawn for positioning.", "Hors {PVP} : affichage pour le placement.", "Außerhalb von {PVP}: Anzeige zum Positionieren.", "{PVP}外：位置調整のため表示中。" },
        ["status_hidden"] = new[] { "Outside {PVP}: plates are hidden (see General).", "Hors {PVP} : barres masquées (voir Général).", "Außerhalb von {PVP}: Leisten ausgeblendet (siehe Allgemein).", "{PVP}外：バーは非表示（「一般」を参照）。" },
        ["status_paused"] = new[]
        {
            "Plates are turned off. Turn them back on with Enabled (General) or /jpvp on.",
            "Les barres sont désactivées. Réactivez-les avec « Activé » (Général) ou /jpvp on.",
            "Die Leisten sind ausgeschaltet. Mit »Aktiviert« (Allgemein) oder /jpvp on wieder einschalten.",
            "バーはオフです。「一般」の「有効」または /jpvp on で再びオンにできます。",
        },

        // pages (sidebar) and their subtitles under the plugin name
        ["tab_preview"] = new[] { "Preview", "Aperçu", "Vorschau", "プレビュー" },
        ["tab_general"] = new[] { "General", "Général", "Allgemein", "一般" },
        ["tab_bars"] = new[] { "Bars", "Barres", "Leisten", "バー" },
        ["tab_text"] = new[] { "Text", "Texte", "Text", "テキスト" },
        ["tab_icons"] = new[] { "Icons", "Icônes", "Symbole", "アイコン" },
        ["tab_debuffs"] = new[] { "Debuffs & signs", "Effets et signes", "Status & Markierungen", "デバフとマーカー" },
        ["tab_targeting"] = new[] { "Targeting me", "Qui me cible", "Wer mich anvisiert", "自分を狙う敵" },
        ["tab_appearance"] = new[] { "Appearance", "Apparence", "Darstellung", "外観" },
        ["tab_diagnostics"] = new[] { "Diagnostics", "Diagnostic", "Diagnose", "診断" },
        ["tab_about"] = new[] { "About", "À propos", "Über", "情報" },
        ["sub_preview"] = new[] { "Live preview of you, your target and an ally", "Aperçu en direct : vous, votre cible et un allié", "Live-Vorschau: du, dein Ziel und ein Verbündeter", "自分・ターゲット・味方のライブプレビュー" },
        ["sub_general"] = new[] { "Who gets a plate, where it sits and how it moves", "Qui a une barre, où elle se place et comment elle bouge", "Wer eine Leiste bekommt, wo sie sitzt und wie sie sich bewegt", "表示する対象、バーの位置と動き" },
        ["sub_bars"] = new[] { "Shape and colours of the {HP}, {MP} and {LB} bars", "Forme et couleurs des barres de {HP}, de {PM} et de {LB}", "Form und Farben der {HP}-, {MP}- und {LB}-Leisten", "{HP}・{MP}・{LB}バーの形と色" },
        ["sub_text"] = new[] { "Name and {HP} text on the plate", "Nom et texte des {HP} sur la barre", "Name und {HP}-Text auf der Leiste", "バー上の名前と{HP}表示" },
        ["sub_icons"] = new[] { "Job icon and {GUARD}", "Icône de job et {GUARD}", "Job-Symbol und {GUARD}", "ジョブアイコンと{GUARD}" },
        ["sub_debuffs"] = new[] { "Debuffs on the plate and {SIGNS} above it", "Effets néfastes sur la barre et {SIGNS} au-dessus", "Negative Status an der Leiste und {SIGNS} darüber", "バーのデバフと、その上の{SIGNS}" },
        ["sub_targeting"] = new[] { "See which enemies have you as their target", "Voir quels ennemis vous ont pour cible", "Sehen, welche Gegner dich als Ziel haben", "自分をターゲットしている敵を表示" },
        ["sub_appearance"] = new[] { "Theme and language of this window", "Thème et langue de cette fenêtre", "Design und Sprache dieses Fensters", "このウィンドウのテーマと言語" },
        ["sub_diagnostics"] = new[] { "Status, log and problem reports", "État, journal et signalement de problèmes", "Status, Protokoll und Problemberichte", "状態、ログ、問題の報告" },
        ["sub_about"] = new[] { "The plugin, support and credits", "Le plugin, soutien et crédits", "Das Plugin, Unterstützung und Mitwirkende", "プラグインの情報、支援、クレジット" },

        // preview page
        ["quick"] = new[] { "Quick switches", "Réglages rapides", "Schnellschalter", "クイック設定" },
        ["preview_hint"] = new[]
        {
            "The preview draws you, your target (an enemy that has you as its target) and an ally with your current settings. Here it shows everything; on the other pages it shows only what that page sets. Under Appearance it can stay pinned above every settings page.",
            "L'aperçu montre vous-même, votre cible (un ennemi qui vous a pour cible) et un allié avec vos réglages actuels. Ici il montre tout ; sur les autres pages, seulement ce que la page règle. Dans Apparence, il peut rester épinglé au-dessus de chaque page de réglages.",
            "Die Vorschau zeigt dich, dein Ziel (einen Gegner, der dich als Ziel hat) und einen Verbündeten mit deinen aktuellen Einstellungen. Hier zeigt sie alles, auf den anderen Seiten nur das, was die jeweilige Seite einstellt. Unter »Darstellung« kann sie über jeder Einstellungsseite angeheftet bleiben.",
            "プレビューには、現在の設定で自分・ターゲット（自分をターゲットしている敵）・味方が表示されます。このページではすべてを、ほかのページではそのページで設定する項目だけを表示します。「外観」で、各設定ページの上に固定表示することもできます。",
        },

        // appearance page
        ["sec_theme"] = new[] { "Theme", "Thème", "Design", "テーマ" },
        ["theme_midnight"] = new[] { "Midnight blue", "Bleu nuit", "Nachtblau", "ミッドナイトブルー" },
        ["theme_black"] = new[] { "Black", "Noir", "Schwarz", "ブラック" },
        ["theme_light"] = new[] { "Light", "Clair", "Hell", "ライト" },
        ["theme_crystal"] = new[] { "Crystal (Final Fantasy)", "Cristal (Final Fantasy)", "Kristall (Final Fantasy)", "クリスタル（FF風）" },
        ["sec_layout"] = new[] { "Layout", "Disposition", "Layout", "レイアウト" },
        ["pin_preview"] = new[] { "Pin the preview above the settings pages", "Épingler l'aperçu au-dessus des pages de réglages", "Vorschau über den Einstellungsseiten anheften", "設定ページの上にプレビューを固定" },
        ["language"] = new[] { "Language", "Langue", "Sprache", "言語" },
        ["language_game"] = new[] { "Game language", "Langue du jeu", "Spielsprache", "ゲームの言語" },
        ["wheel_help"] = new[]
        {
            "Tip: hold Ctrl and turn the mouse wheel over a drop-down list to switch its value.",
            "Astuce : maintenez Ctrl et tournez la molette sur une liste déroulante pour changer sa valeur.",
            "Tipp: Strg gedrückt halten und über einer Auswahlliste das Mausrad drehen, um den Wert zu wechseln.",
            "ヒント：Ctrlを押しながらドロップダウンの上でマウスホイールを回すと、選択を切り替えられます。",
        },
        ["reset_section"] = new[] { "Reset this section", "Réinitialiser cette section", "Diesen Abschnitt zurücksetzen", "このセクションをリセット" },
        ["reset_all"] = new[] { "Reset everything", "Tout réinitialiser", "Alles zurücksetzen", "すべてリセット" },
        ["reset_confirm"] = new[] { "Click again to reset everything", "Cliquez encore pour tout réinitialiser", "Zum Bestätigen erneut klicken", "もう一度クリックでリセット" },
        ["sec_reset"] = new[] { "Reset", "Réinitialisation", "Zurücksetzen", "リセット" },
        ["reset_all_help"] = new[]
        {
            "Puts every setting back to its default. The language and theme of this window are kept.",
            "Remet tous les réglages par défaut. La langue et le thème de cette fenêtre sont conservés.",
            "Setzt alle Einstellungen auf die Standardwerte zurück. Sprache und Design dieses Fensters bleiben erhalten.",
            "すべての設定を初期値に戻します。このウィンドウの言語とテーマはそのままです。",
        },

        // general
        ["sec_general"] = new[] { "General", "Général", "Allgemein", "一般" },
        ["enabled"] = new[] { "Enabled", "Activé", "Aktiviert", "有効" },
        ["hide_ui"] = new[] { "Hide when the game UI is hidden", "Masquer quand l'interface du jeu est masquée", "Ausblenden, wenn die Spieloberfläche ausgeblendet ist", "ゲームのUI非表示時に隠す" },
        ["behind_ui"] = new[] { "Hide behind plugin windows", "Masquer derrière les fenêtres des plugins", "Hinter Plugin-Fenstern verbergen", "プラグインのウィンドウの後ろに隠す" },
        ["behind_ui_help"] = new[]
        {
            "Plates are cut where a plugin window covers them (menus of other plugins, these settings). The game's own UI, its effects and click-through plugin overlays never hide them.",
            "Les barres sont coupées là où une fenêtre de plugin les recouvre (menus d'autres plugins, ces réglages). L'interface du jeu, ses effets et les calques de plugins non cliquables ne les masquent jamais.",
            "Die Leisten werden dort abgeschnitten, wo ein Plugin-Fenster sie verdeckt (Menüs anderer Plugins, diese Einstellungen). Die Spieloberfläche, ihre Effekte und durchklickbare Plugin-Overlays verdecken sie nie.",
            "プラグインのウィンドウ（他のプラグインのメニューやこの設定画面）に重なる部分は隠れます。ゲームのUI、エフェクト、クリックを通すプラグインのオーバーレイでは隠れません。",
        },
        ["outside_pvp"] = new[] { "Draw outside {PVP} (for positioning)", "Afficher hors {PVP} (pour le placement)", "Außerhalb von {PVP} anzeigen (zum Positionieren)", "{PVP}以外でも表示（位置調整用）" },
        ["outside_pvp_help"] = new[]
        {
            "Shows plates for nearby players outside {PVP}, so everything can be set up in town.",
            "Affiche les barres des joueurs proches hors {PVP}, pour tout régler en ville.",
            "Zeigt die Leisten von Spielern in der Nähe auch außerhalb von {PVP} an, damit sich alles in der Stadt einstellen lässt.",
            "{PVP}以外でも周囲のプレイヤーのバーを表示し、街中で調整できるようにします。",
        },
        ["native_np"] = new[] { "Only while the game shows the name", "Seulement si le jeu affiche le nom", "Nur, wenn das Spiel den Namen anzeigt", "ゲームが名前を表示している時のみ" },
        ["native_np_help"] = new[]
        {
            "Allies only get a plate while the game shows their name. For enemies, the plugin checks instead whether they can be targeted.",
            "Les alliés n'ont une barre que tant que le jeu affiche leur nom. Pour les ennemis, le plugin vérifie à la place s'ils peuvent être ciblés.",
            "Verbündete erhalten nur dann eine Leiste, wenn das Spiel ihren Namen anzeigt. Bei Gegnern prüft das Plugin stattdessen, ob sie anvisierbar sind.",
            "味方は、ゲームが名前を表示している間だけバーを表示します。敵はターゲットできるかどうかで判定します。",
        },
        ["show_for"] = new[] { "Show plates for", "Afficher les barres pour", "Leisten anzeigen für", "バーを表示する対象" },
        ["enemies"] = new[] { "Enemies", "Ennemis", "Gegner", "敵" },
        ["party_members"] = new[] { "Party members", "Équipiers", "Gruppenmitglieder", "パーティメンバー" },
        ["alliance_members"] = new[] { "Alliance members", "Membres de l'alliance", "Allianzmitglieder", "アライアンスメンバー" },
        ["myself"] = new[] { "Myself", "Moi-même", "Mich selbst", "自分" },
        ["other_friendly"] = new[] { "Other friendly players", "Autres joueurs alliés", "Andere befreundete Spieler", "その他の味方プレイヤー" },
        ["max_distance"] = new[] { "Maximum distance", "Distance maximale", "Maximale Entfernung", "最大距離" },
        ["highlight_target"] = new[] { "Highlight current target", "Mettre en évidence la cible actuelle", "Aktuelles Ziel hervorheben", "現在のターゲットを強調" },

        // position and movement
        ["sec_position"] = new[] { "Plate position and movement", "Position et mouvement de la barre", "Position und Bewegung der Leiste", "バーの位置と動き" },
        ["plate_position"] = new[] { "Plate position", "Position de la barre", "Position der Leiste", "バーの位置" },
        ["above_head"] = new[] { "Above the head", "Au-dessus de la tête", "Über dem Kopf", "頭上" },
        ["under_feet"] = new[] { "Under the feet", "Sous les pieds", "Unter den Füßen", "足元" },
        ["height_under"] = new[] { "Height under the feet", "Hauteur sous les pieds", "Höhe unter den Füßen", "足元からの高さ" },
        ["height_under_help"] = new[]
        {
            "0 = at the feet; negative = lower, positive = up towards the knees.",
            "0 = au niveau des pieds ; négatif = plus bas, positif = vers les genoux.",
            "0 = auf Fußhöhe; negativ = tiefer, positiv = Richtung Knie.",
            "0＝足の位置。マイナスで下へ、プラスでひざの方へ。",
        },
        ["gap_under"] = new[] { "Gap under the feet", "Écart sous les pieds", "Abstand unter den Füßen", "足元との間隔" },
        ["height_above"] = new[] { "Height above the feet", "Hauteur depuis les pieds", "Höhe über den Füßen", "表示の高さ" },
        ["height_above_help"] = new[]
        {
            "Where the plate sits in the 3D world, measured from the feet.",
            "Position de la barre dans le monde 3D, mesurée depuis les pieds.",
            "Wo die Leiste in der 3D-Welt sitzt, gemessen von den Füßen.",
            "3D空間でのバーの高さ（足元から測ります）。",
        },
        ["screen_y"] = new[] { "Screen Y offset", "Décalage vertical à l'écran", "Vertikaler Versatz am Bildschirm", "画面上の縦位置補正" },
        ["stabilize"] = new[] { "Stabilize movement", "Stabiliser le mouvement", "Bewegung stabilisieren", "動きを安定化" },
        ["stabilize_help"] = new[]
        {
            "The plate is placed from the character's position in the 3D world and projected with the current camera every frame, so turning the camera never makes it lag. Optional smoothing below; your own plate always follows exactly.",
            "La barre est placée d'après la position du personnage dans le monde 3D et projetée à chaque image avec la caméra actuelle : tourner la caméra ne la fait jamais traîner. Lissage facultatif ci-dessous ; votre propre barre suit toujours exactement.",
            "Die Leiste wird anhand der Position des Charakters in der 3D-Welt platziert und in jedem Bild mit der aktuellen Kamera projiziert, sodass sie bei Kameradrehungen nie hinterherhinkt. Optionale Glättung unten; deine eigene Leiste folgt immer exakt.",
            "バーはキャラクターの3D空間上の位置から配置し、毎フレーム現在のカメラで投影するので、カメラを回しても遅れません。下で任意の平滑化を設定できます。自分のバーは常に完全に追従します。",
        },
        ["follow_side"] = new[] { "Follow sideways", "Suivi horizontal", "Seitliches Folgen", "横方向の追従" },
        ["follow_side_help"] = new[]
        {
            "How tightly the plate follows running, dashes and knock-backs. 60 = exactly (no smoothing).",
            "Fermeté du suivi pendant la course, les ruées et les projections. 60 = exact (sans lissage).",
            "Wie genau die Leiste Laufen, Vorstößen und Rückstößen folgt. 60 = exakt (ohne Glättung).",
            "走行・突進・ノックバックへの追従の強さ。60＝完全に追従（平滑化なし）。",
        },
        ["follow_vertical"] = new[] { "Follow up / down", "Suivi vertical", "Vertikales Folgen", "縦方向の追従" },
        ["follow_vertical_help"] = new[]
        {
            "60 = follows jumps exactly, like the name. Lower = calmer while the player jumps.",
            "60 = suit exactement les sauts, comme le nom. Plus bas = plus calme pendant les sauts.",
            "60 = folgt Sprüngen exakt, wie der Name. Niedriger = ruhiger beim Springen.",
            "60＝名前と同じくジャンプに完全に追従します。低いほど、ジャンプ中のバーの動きが穏やかになります。",
        },
        ["snap"] = new[] { "Snap distance", "Distance de recalage", "Distanz für sofortiges Versetzen", "瞬間移動とみなす距離" },
        ["snap_help"] = new[]
        {
            "Moves longer than this (teleports, respawns) are taken at once.",
            "Les déplacements plus longs (téléportations, réapparitions) sont appliqués d'un coup.",
            "Längere Bewegungen (Teleports, Wiedereinstieg) werden sofort übernommen.",
            "これより長い移動（テレポ、リスポーン）は即座に反映します。",
        },
        ["pixel_snap"] = new[] { "Pixel snapping", "Alignement sur les pixels", "An Pixeln ausrichten", "ピクセルに合わせる" },
        ["pixel_snap_help"] = new[]
        {
            "Whole-pixel placement: no shimmer, sharp edges and text.",
            "Placement au pixel près : pas de scintillement, bords et texte nets.",
            "Platzierung auf ganzen Pixeln: kein Flimmern, scharfe Kanten und Texte.",
            "整数ピクセルに配置：ちらつきがなく、縁と文字がくっきりします。",
        },

        // bars
        ["bars_shape"] = new[] { "Shape", "Forme", "Form", "形" },
        ["bar_width"] = new[] { "Bar width", "Largeur des barres", "Leistenbreite", "バーの幅" },
        ["border_thickness"] = new[] { "Border thickness", "Épaisseur de la bordure", "Randstärke", "枠の太さ" },
        ["corner_rounding"] = new[] { "Corner rounding", "Arrondi des coins", "Eckenrundung", "角の丸み" },
        ["border"] = new[] { "Border", "Bordure", "Rand", "枠" },
        ["hp_bar"] = new[] { "{HP} bar", "Barre de {HP}", "{HP}-Leiste", "{HP}バー" },
        ["hp_bar_height"] = new[] { "{HP} bar height", "Hauteur de la barre de {HP}", "Höhe der {HP}-Leiste", "{HP}バーの高さ" },
        ["c_enemy"] = new[] { "Enemy", "Ennemi", "Gegner", "敵" },
        ["c_party"] = new[] { "Party", "Équipe", "Gruppe", "パーティ" },
        ["c_alliance"] = new[] { "Alliance", "Alliance", "Allianz", "アライアンス" },
        ["c_friendly"] = new[] { "Friendly", "Allié", "Befreundet", "味方" },
        ["c_myself"] = new[] { "Myself", "Moi", "Ich", "自分" },
        ["c_empty"] = new[] { "Empty", "Vide", "Leer", "空" },
        ["barrier"] = new[] { "Barrier (shield)", "Barrière", "Barriere", "バリア" },
        ["barrier_help"] = new[]
        {
            "Drawn over the {HP} bar from the left in its own colour, without mixing with the {HP} colour. The colour's opacity sets how solid it is; a thin line still marks where the {HP} ends.",
            "Dessinée par-dessus la barre de {HP} depuis la gauche, dans sa propre couleur, sans se mélanger à celle des {HP}. L'opacité de la couleur règle sa densité ; un trait fin indique toujours où s'arrêtent les {HP}.",
            "Wird von links über die {HP}-Leiste gezeichnet, in eigener Farbe und ohne sich mit der {HP}-Farbe zu mischen. Die Deckkraft der Farbe bestimmt, wie dicht sie ist; eine dünne Linie zeigt weiterhin, wo die {HP} enden.",
            "{HP}バーの左から独自の色で重ねて表示します（{HP}の色とは混ざりません）。色の不透明度で濃さを調整できます。{HP}の終わりは細い線で示されます。",
        },
        ["mp_bar"] = new[] { "{MP} bar", "Barre de {PM}", "{MP}-Leiste", "{MP}バー" },
        ["mp_bar_height"] = new[] { "{MP} bar height", "Hauteur de la barre de {PM}", "Höhe der {MP}-Leiste", "{MP}バーの高さ" },
        ["mp_bar_spacing"] = new[] { "{MP} bar spacing", "Espacement de la barre de {PM}", "Abstand der {MP}-Leiste", "{MP}バーの間隔" },
        ["mp_colour"] = new[] { "{MP}", "{PM}", "{MP}", "{MP}" },
        ["lb_bar"] = new[] { "{LB} gauge", "Jauge de {LB}", "{LB}-Leiste", "{LB}ゲージ" },
        ["lb_bar_help"] = new[]
        {
            "Each player's {PVP} {LB} gauge, read from the {CC} team lists. Blinks with a gradient when full.",
            "La jauge de {LB} {PVP} de chaque joueur, lue dans les listes d'équipes de {CC}. Clignote en dégradé quand elle est pleine.",
            "Die {PVP}-{LB}-Leiste jedes Spielers, ausgelesen aus den Teamlisten im {CC}. Blinkt im Farbverlauf, wenn sie voll ist.",
            "各プレイヤーの{PVP}の{LB}ゲージ。{CC}のチームリストから読み取ります。満タンになるとグラデーションで点滅します。",
        },
        ["lb_bar_height"] = new[] { "{LB} gauge height", "Hauteur de la jauge de {LB}", "Höhe der {LB}-Leiste", "{LB}ゲージの高さ" },
        ["lb_bar_spacing"] = new[] { "{LB} gauge spacing", "Espacement de la jauge de {LB}", "Abstand der {LB}-Leiste", "{LB}ゲージの間隔" },
        ["lb_blink"] = new[] { "Blink speed when full", "Vitesse de clignotement (jauge pleine)", "Blinkgeschwindigkeit (voll)", "満タン時の点滅速度" },
        ["c_charge"] = new[] { "Charge", "Charge", "Füllung", "ゲージ" },
        ["c_full1"] = new[] { "Full 1", "Pleine 1", "Voll 1", "満タン1" },
        ["c_full2"] = new[] { "Full 2", "Pleine 2", "Voll 2", "満タン2" },

        // name and HP text
        ["sec_text"] = new[] { "Name and {HP} text", "Nom et texte des {HP}", "Name und {HP}-Text", "名前と{HP}表示" },
        ["name"] = new[] { "Name", "Nom", "Name", "名前" },
        ["name_size"] = new[] { "Name size", "Taille du nom", "Namensgröße", "名前のサイズ" },
        ["hp_text"] = new[] { "{HP} text", "Texte des {HP}", "{HP}-Text", "{HP}表示" },
        ["hp_text_help"] = new[]
        {
            "Drawn on the {HP} bar (needs the {HP} bar).",
            "Affiché sur la barre de {HP} (nécessite la barre de {HP}).",
            "Wird auf der {HP}-Leiste angezeigt (benötigt die {HP}-Leiste).",
            "{HP}バーの上に表示します（{HP}バーが必要です）。",
        },
        ["hp_format"] = new[] { "Format", "Format", "Format", "表示形式" },
        ["fmt_percent"] = new[] { "Percent", "Pourcentage", "Prozent", "パーセント" },
        ["fmt_current"] = new[] { "Current {HP}", "{HP} actuels", "Aktuelle {HP}", "現在の{HP}" },
        ["fmt_current_max"] = new[] { "Current / maximum", "Actuels / max.", "Aktuell / Maximum", "現在／最大" },
        ["fmt_current_short"] = new[] { "Current (short)", "Actuels (abrégé)", "Aktuell (kurz)", "現在（短縮）" },
        ["fmt_current_max_short"] = new[] { "Current / maximum (short)", "Actuels / max. (abrégé)", "Aktuell / Maximum (kurz)", "現在／最大（短縮）" },
        ["hp_text_size"] = new[] { "{HP} text size", "Taille du texte des {HP}", "Größe des {HP}-Texts", "{HP}表示のサイズ" },
        ["text_colour"] = new[] { "{HP} and timer text colour", "Couleur du texte des {HP} et des minuteurs", "Farbe von {HP}-Text und Timern", "{HP}とタイマーの文字色" },
        ["name_colours"] = new[] { "Name colour", "Couleur du nom", "Namensfarbe", "名前の色" },

        // job icon
        ["sec_job"] = new[] { "Job icon", "Icône de job", "Job-Symbol", "ジョブアイコン" },
        ["show_job"] = new[] { "Show job icon", "Afficher l'icône de job", "Job-Symbol anzeigen", "ジョブアイコンを表示" },
        ["style"] = new[] { "Style", "Style", "Stil", "スタイル" },
        ["style_party"] = new[] { "{PARTYLIST} (framed, role colour)", "{PARTYLIST} (cadre, couleur du rôle)", "{PARTYLIST} (Rahmen, Rollenfarbe)", "{PARTYLIST}（枠付き・ロール色）" },
        ["style_gold"] = new[] { "Gold glyph", "Symbole doré", "Goldenes Symbol", "金色の記号" },
        ["style_light"] = new[] { "Light tile", "Tuile claire", "Helle Kachel", "明るいタイル" },
        ["match_height"] = new[] { "Match bar height", "Même hauteur que les barres", "Gleiche Höhe wie die Leisten", "バーと同じ高さ" },
        ["match_height_help"] = new[]
        {
            "As tall as the {HP}, {MP} and {LB} bars together and centred on them.",
            "Aussi haute que les barres de {HP}, {PM} et {LB} réunies, et centrée sur elles.",
            "So hoch wie die {HP}-, {MP}- und {LB}-Leisten zusammen und auf sie zentriert.",
            "{HP}・{MP}・{LB}のバーを合わせた高さで、その中央に配置します。",
        },
        ["size"] = new[] { "Size", "Taille", "Größe", "サイズ" },
        ["spacing"] = new[] { "Spacing", "Espacement", "Abstand", "間隔" },
        ["x_offset"] = new[] { "X offset", "Décalage X", "X-Versatz", "X位置補正" },
        ["y_offset"] = new[] { "Y offset", "Décalage Y", "Y-Versatz", "Y位置補正" },
        ["elixir_fill"] = new[] { "Fill during {ELIXIR}", "Remplissage pendant l'incantation d'{ELIXIR}", "Füllung bei {ELIXIR}", "{ELIXIR}の詠唱表示" },
        ["elixir_fill_help"] = new[]
        {
            "While a player casts {ELIXIR}, the job icon fills from the bottom up with the cast progress.",
            "Pendant qu'un joueur utilise {ELIXIR}, l'icône de job se remplit de bas en haut au fil de l'incantation.",
            "Während ein Spieler {ELIXIR} wirkt, füllt sich das Job-Symbol mit dem Wirkfortschritt von unten nach oben.",
            "プレイヤーが{ELIXIR}を詠唱している間、ジョブアイコンが詠唱の進行に合わせて下から満たされます。",
        },

        // guard
        ["sec_guard"] = new[] { "{GUARD}", "{GUARD}", "{GUARD}", "{GUARD}" },
        ["show_guard"] =new[] { "Show {GUARD}", "Afficher l'état de {GUARD}", "{GUARD} anzeigen", "{GUARD}を表示" },
        ["show_guard_help"] = new[]
        {
            "Works for allies and enemies. The recast starts when {GUARD} is used and is reset on revival.",
            "Fonctionne pour les alliés et les ennemis. La recharge démarre à l'utilisation de {GUARD} et se réinitialise à la réanimation.",
            "Funktioniert bei Verbündeten und Gegnern. Die Reaktivierung beginnt beim Einsatz von {GUARD} und wird bei der Wiederbelebung zurückgesetzt.",
            "味方にも敵にも有効です。リキャストは{GUARD}の使用時に始まり、戦闘不能からの復帰でリセットされます。",
        },
        ["use_action_icon"] = new[] { "Use the {GUARD} action icon", "Utiliser l'icône de l'action {GUARD}", "Aktionssymbol von {GUARD} verwenden", "{GUARD}のアクションアイコンを使用" },
        ["use_action_icon_help"] = new[]
        {
            "Off: a simple shield symbol.",
            "Désactivé : un simple symbole de bouclier.",
            "Aus: ein einfaches Schild-Symbol.",
            "オフ：シンプルな盾の記号。",
        },
        ["glow_active"] = new[] { "Glow while active", "Halo quand actif", "Leuchten, solange aktiv", "効果中は光らせる" },
        ["countdown"] = new[] { "Recast countdown", "Compte à rebours de la recharge", "Countdown der Reaktivierung", "リキャストのカウントダウン" },
        ["c_ready"] = new[] { "Ready", "Prêt", "Bereit", "使用可能" },
        ["c_active"] = new[] { "Active", "Actif", "Aktiv", "効果中" },
        ["c_cooldown"] = new[] { "Recast", "Recharge", "Reaktivierung", "リキャスト中" },

        // debuffs
        ["sec_debuffs"] = new[] { "Debuffs", "Effets néfastes", "Negative Status", "デバフ" },
        ["debuffs_enemies"] = new[] { "Debuffs on enemies", "Effets néfastes sur les ennemis", "Negative Status bei Gegnern", "敵のデバフ" },
        ["debuffs_allies"] = new[] { "Debuffs on allies", "Effets néfastes sur les alliés", "Negative Status bei Verbündeten", "味方のデバフ" },
        ["debuffs_self"] = new[] { "Debuffs on myself", "Effets néfastes sur moi-même", "Negative Status bei mir selbst", "自分のデバフ" },
        ["show_debuffs"] = new[] { "Show debuffs", "Afficher les effets néfastes", "Negative Status anzeigen", "デバフを表示" },
        ["position"] = new[] { "Position", "Position", "Position", "位置" },
        ["pos_auto"] = new[] { "Auto (away from the character)", "Auto (à l'écart du personnage)", "Automatisch (weg vom Charakter)", "自動（キャラクターから離れた側）" },
        ["pos_under"] = new[] { "Under the plate (centred)", "Sous la barre (centrés)", "Unter der Leiste (zentriert)", "バーの下（中央揃え）" },
        ["pos_over"] = new[] { "Over the plate (centred)", "Au-dessus de la barre (centrés)", "Über der Leiste (zentriert)", "バーの上（中央揃え）" },
        ["pos_right"] = new[] { "Right of the {GUARD} icon", "À droite de l'icône de {GUARD}", "Rechts vom Symbol von {GUARD}", "{GUARD}アイコンの右" },
        ["pos_left"] = new[] { "Left of the job icon", "À gauche de l'icône de job", "Links vom Job-Symbol", "ジョブアイコンの左" },
        ["only_mine"] = new[] { "Only my debuffs", "Seulement mes effets néfastes", "Nur meine negativen Status", "自分のデバフのみ" },
        ["border_mine"] = new[] { "Border around my debuffs", "Bordure autour de mes effets néfastes", "Rahmen um meine negativen Status", "自分のデバフに枠" },
        ["timers"] = new[] { "Time remaining", "Durée restante", "Verbleibende Dauer", "残り時間" },
        ["icon_size"] = new[] { "Icon size", "Taille des icônes", "Symbolgröße", "アイコンのサイズ" },
        ["gap_plate"] = new[] { "Gap to the plate", "Écart avec la barre", "Abstand zur Leiste", "バーとの間隔" },
        ["max_shown"] = new[] { "Maximum shown", "Nombre maximal affiché", "Maximal angezeigt", "最大表示数" },

        // signs
        ["sec_signs"] = new[] { "{SIGNS}", "{SIGNS}", "{SIGNS}", "{SIGNS}" },
        ["signs_above"] =new[] { "{SIGNS} above the plate", "{SIGNS} au-dessus de la barre", "{SIGNS} über der Leiste", "{SIGNS}をバーの上に" },
        ["signs_above_help"] = new[]
        {
            "The sign the game shows over the name is hidden and drawn centred above the whole plate instead.",
            "Le signe que le jeu affiche au-dessus du nom est masqué et redessiné, centré, au-dessus de toute la barre.",
            "Die Markierung, die das Spiel über dem Namen zeigt, wird ausgeblendet und stattdessen zentriert über der gesamten Leiste gezeichnet.",
            "ゲームが名前の上に表示するマーカーを隠し、代わりにバー全体の上に中央揃えで表示します。",
        },
        ["gap"] = new[] { "Gap", "Écart", "Abstand", "間隔" },

        // enemies targeting me
        ["sec_targeting"] = new[] { "Enemies targeting me", "Ennemis qui me ciblent", "Gegner, die mich anvisieren", "自分をターゲットしている敵" },
        ["dot"] = new[] { "Dot under the enemy", "Point sous l'ennemi", "Punkt unter dem Gegner", "敵の足元に点" },
        ["dot_help"] = new[]
        {
            "A dot at the feet of every enemy that has you as its target, in its role colour.",
            "Un point aux pieds de chaque ennemi qui vous a pour cible, de la couleur de son rôle.",
            "Ein Punkt zu Füßen jedes Gegners, der dich als Ziel hat, in der Farbe seiner Rolle.",
            "自分をターゲットしている敵の足元に、ロールの色で点を表示します。",
        },
        ["c_tank"] = new[] { "{TANK}", "{TANK}", "{TANK}", "{TANK}" },
        ["c_healer"] = new[] { "{HEALER}", "{HEALER}", "{HEALER}", "{HEALER}" },
        ["c_dps"] = new[] { "{DPS}", "{DPS}", "{DPS}", "{DPS}" },
        ["dot_size"] =new[] { "Dot size", "Taille du point", "Punktgröße", "点のサイズ" },
        ["dot_offset"] = new[] { "Dot Y offset", "Décalage Y du point", "Y-Versatz des Punkts", "点のY位置補正" },
        ["icons_above"] = new[] { "Their job icons above my head", "Leurs icônes de job au-dessus de ma tête", "Ihre Job-Symbole über meinem Kopf", "その敵のジョブアイコンを自分の頭上に" },
        ["icons_above_help"] = new[]
        {
            "One job icon per enemy that has you as its target, including enemies off screen.",
            "Une icône de job par ennemi qui vous a pour cible, y compris hors de l'écran.",
            "Ein Job-Symbol pro Gegner, der dich als Ziel hat, auch außerhalb des Bildschirms.",
            "自分をターゲットしている敵1人につき1つのジョブアイコンを表示します（画面外の敵も含む）。",
        },
        ["red_glow_icons"] = new[] { "Red glow around them", "Halo rouge autour", "Rotes Leuchten darum", "周りを赤く光らせる" },
        ["height_above_me"] = new[] { "Height above me", "Hauteur au-dessus de moi", "Höhe über mir", "自分の頭上の高さ" },
        ["job_glow"] = new[] { "Red glow on the enemy's job icon", "Halo rouge sur l'icône de job de l'ennemi", "Rotes Leuchten am Job-Symbol des Gegners", "敵のジョブアイコンを赤く光らせる" },
        ["swords"] = new[] { "Crossed swords over the enemy's job icon", "Épées croisées au-dessus de l'icône de job de l'ennemi", "Gekreuzte Schwerter über dem Job-Symbol des Gegners", "敵のジョブアイコンの上に交差した剣" },
        ["glow_colour"] = new[] { "Glow / swords colour", "Couleur du halo / des épées", "Farbe von Leuchten / Schwertern", "光・剣の色" },

        // diagnostics page
        ["sec_status"] = new[] { "Status", "État", "Status", "状態" },
        ["d_plates"] = new[] { "Plates now / peak", "Barres : actuel / max", "Leisten jetzt / Spitze", "バー（現在／最大）" },
        ["d_plates_help"] = new[]
        {
            "Plates drawn in this frame / the most at once since the plugin was loaded.",
            "Barres affichées dans cette image / le maximum simultané depuis le chargement du plugin.",
            "In diesem Bild gezeichnete Leisten / die meisten gleichzeitig seit dem Laden des Plugins.",
            "このフレームで描画したバーの数／プラグイン読み込み後の最大同時数。",
        },
        ["d_frame"] = new[] { "Time per frame", "Temps par image", "Zeit pro Bild", "1フレームの処理時間" },
        ["d_frame_help"] = new[]
        {
            "Average time the plugin needs per frame to draw the plates.",
            "Temps moyen dont le plugin a besoin par image pour afficher les barres.",
            "Durchschnittliche Zeit, die das Plugin pro Bild zum Zeichnen der Leisten braucht.",
            "プラグインがバーの描画に1フレームあたり要する平均時間。",
        },
        ["d_guards"] = new[] { "{GUARD} uses seen", "Utilisations de {GUARD}", "{GUARD}-Einsätze", "{GUARD}の使用回数" },
        ["d_guards_help"] = new[]
        {
            "{GUARD} uses seen since the plugin was loaded.",
            "Utilisations de {GUARD} détectées depuis le chargement du plugin.",
            "Seit dem Laden des Plugins erkannte Einsätze von {GUARD}.",
            "プラグイン読み込み後に検出した{GUARD}の使用回数。",
        },
        ["d_lb"] = new[] { "{LB} gauges", "Jauges de {LB}", "{LB}-Leisten", "LBゲージ" },     // JA: short form fits the tile
        ["d_lb_help"] = new[]
        {
            "Players whose {LB} gauge was read from the {CC} team lists in this frame.",
            "Joueurs dont la jauge de {LB} a été lue dans les listes d'équipes de {CC} pour cette image.",
            "Spieler, deren {LB}-Leiste in diesem Bild aus den Teamlisten im {CC} gelesen wurde.",
            "このフレームで{CC}のチームリストから{LB}ゲージを読み取ったプレイヤーの数。",
        },
        ["d_signs"] = new[] { "Signs placed", "Signes posés", "Markierungen gesetzt", "付いているマーカー" },
        ["d_signs_help"] = new[]
        {
            "{SIGNS} currently placed on players (counted while \"{SIGNS} above the plate\" is on).",
            "{SIGNS} actuellement posés sur des joueurs (comptés quand « {SIGNS} au-dessus de la barre » est activé).",
            "Derzeit auf Spieler gesetzte {SIGNS} (gezählt, solange »{SIGNS} über der Leiste« aktiv ist).",
            "現在プレイヤーに付いている{SIGNS}の数（「{SIGNS}をバーの上に」がオンの時に数えます）。",
        },
        ["d_targeting"] = new[] { "Enemies targeting you", "Ennemis vous ciblant", "Gegner mit dir als Ziel", "自分を狙う敵" },
        ["d_targeting_help"] = new[]
        {
            "Enemies that have you as their target right now.",
            "Ennemis qui vous ont pour cible en ce moment.",
            "Gegner, die dich gerade als Ziel haben.",
            "現在自分をターゲットしている敵の数。",
        },
        ["record_events"] = new[] { "Record game events in the log", "Enregistrer les événements du jeu dans le journal", "Spielereignisse im Protokoll aufzeichnen", "ゲーム内の出来事をログに記録" },
        ["record_events_help"] = new[]
        {
            "Adds a line to the log when an enemy starts or stops targeting you, when a player uses {GUARD} and when a player casts {ELIXIR}. These lines contain character names.",
            "Ajoute une ligne au journal quand un ennemi commence ou cesse de vous cibler, quand un joueur utilise {GUARD} et quand un joueur lance {ELIXIR}. Ces lignes contiennent des noms de personnages.",
            "Fügt dem Protokoll eine Zeile hinzu, wenn ein Gegner beginnt oder aufhört, dich anzuvisieren, wenn ein Spieler {GUARD} einsetzt und wenn ein Spieler {ELIXIR} wirkt. Diese Zeilen enthalten Charakternamen.",
            "敵が自分をターゲットした時・外した時、プレイヤーが{GUARD}を使用した時、{ELIXIR}を詠唱した時に、ログへ1行追加します。この行にはキャラクター名が含まれます。",
        },
        ["sec_feedback"] = new[] { "Feedback", "Retours", "Rückmeldung", "フィードバック" },
        ["fb_title"] = new[] { "Something not working?", "Quelque chose ne fonctionne pas ?", "Funktioniert etwas nicht?", "うまく動かない場合は" },
        ["fb_steps"] = new[]
        {
            "1. Make the problem happen again (for {GUARD}, {ELIXIR} or targeting, first turn on event recording above).\n2. Press Copy report: the version, your settings and this log go to the clipboard.\n3. Press Report a problem: a GitHub issue opens, paste the report there with Ctrl+V.",
            "1. Reproduisez le problème (pour {GUARD}, {ELIXIR} ou le ciblage, activez d'abord l'enregistrement des événements ci-dessus).\n2. Cliquez sur « Copier le rapport » : la version, vos réglages et ce journal sont copiés dans le presse-papiers.\n3. Cliquez sur « Signaler un problème » : un ticket GitHub s'ouvre, collez-y le rapport avec Ctrl+V.",
            "1. Das Problem erneut auslösen (bei {GUARD}, {ELIXIR} oder Anvisieren vorher oben die Ereignisaufzeichnung einschalten).\n2. »Bericht kopieren« drücken: Version, Einstellungen und dieses Protokoll landen in der Zwischenablage.\n3. »Problem melden« drücken: Ein GitHub-Issue öffnet sich, dort den Bericht mit Strg+V einfügen.",
            "1. 問題をもう一度起こします（{GUARD}、{ELIXIR}、ターゲット関連の場合は、先に上の「ゲーム内の出来事をログに記録」をオンにします）。\n2. 「レポートをコピー」を押すと、バージョン、設定、このログがクリップボードにコピーされます。\n3. 「問題を報告」を押すとGitHubのIssueが開くので、Ctrl+Vでレポートを貼り付けてください。",
        },
        ["report_problem"] = new[] { "Report a problem", "Signaler un problème", "Problem melden", "問題を報告" },
        ["copy_report"] = new[] { "Copy report", "Copier le rapport", "Bericht kopieren", "レポートをコピー" },
        ["report_help"] = new[]
        {
            "Copies the report to the clipboard and opens a new issue on the plugin's GitHub page: describe the problem and paste the report with Ctrl+V (a GitHub account is needed).",
            "Copie le rapport dans le presse-papiers et ouvre un nouveau ticket sur la page GitHub du plugin : décrivez le problème et collez le rapport avec Ctrl+V (un compte GitHub est nécessaire).",
            "Kopiert den Bericht in die Zwischenablage und öffnet ein neues Issue auf der GitHub-Seite des Plugins: Problem beschreiben und den Bericht mit Strg+V einfügen (ein GitHub-Konto ist nötig).",
            "レポートをクリップボードにコピーし、プラグインのGitHubページで新しいIssueを開きます。問題を書いて、Ctrl+Vでレポートを貼り付けてください（GitHubアカウントが必要です）。",
        },
        ["github_help"] = new[] { "The plugin's page on GitHub: code, updates and reported problems.", "La page GitHub du plugin : code, mises à jour et problèmes signalés.", "Die GitHub-Seite des Plugins: Code, Updates und gemeldete Probleme.", "プラグインのGitHubページ（コード、更新、報告された問題）。" },
        ["sec_log"] = new[] { "Log", "Journal", "Protokoll", "ログ" },
        ["log_all"] = new[] { "All", "Tout", "Alle", "すべて" },
        ["log_info"] = new[] { "Info", "Infos", "Info", "情報" },
        ["log_warn"] = new[] { "Warnings", "Avertissements", "Warnungen", "警告" },
        ["log_error"] = new[] { "Errors", "Erreurs", "Fehler", "エラー" },
        ["log_filter"] = new[] { "Search the log", "Rechercher dans le journal", "Protokoll durchsuchen", "ログを検索" },
        ["copy_lines"] = new[] { "Copy lines", "Copier les lignes", "Zeilen kopieren", "行をコピー" },
        ["clear"] = new[] { "Clear", "Effacer", "Leeren", "クリア" },
        ["auto_scroll"] = new[] { "Auto-scroll", "Défilement auto", "Auto-Scroll", "自動スクロール" },
        ["log_select_help"] = new[]
        {
            "Click lines to select them; the copy buttons then take only the selected lines. Without a selection they take every line shown.",
            "Cliquez sur des lignes pour les sélectionner : les boutons de copie ne prennent alors que ces lignes. Sans sélection, ils prennent toutes les lignes affichées.",
            "Zeilen anklicken, um sie auszuwählen; die Kopier-Schaltflächen übernehmen dann nur diese. Ohne Auswahl werden alle angezeigten Zeilen übernommen.",
            "行をクリックすると選択でき、コピーボタンは選択した行だけを対象にします。選択がない場合は、表示中のすべての行が対象です。",
        },
        ["log_empty"] = new[] { "Nothing logged yet.", "Rien dans le journal pour l'instant.", "Noch keine Einträge.", "まだログはありません。" },
        ["privacy"] = new[]
        {
            "The log stays in memory on your PC and is cleared when the game closes. Nothing is sent anywhere: a report only leaves your PC when you paste it yourself. Recorded events contain character names, so check the text before sharing it.",
            "Le journal reste en mémoire sur votre PC et s'efface à la fermeture du jeu. Rien n'est envoyé nulle part : un rapport ne quitte votre PC que si vous le collez vous-même. Les événements enregistrés contiennent des noms de personnages : relisez le texte avant de le partager.",
            "Das Protokoll bleibt im Arbeitsspeicher deines PCs und wird beim Beenden des Spiels gelöscht. Es wird nichts gesendet: Ein Bericht verlässt deinen PC nur, wenn du ihn selbst einfügst. Aufgezeichnete Ereignisse enthalten Charakternamen, prüfe den Text also vor dem Teilen.",
            "ログはPCのメモリ上にのみ保持され、ゲームを終了すると消去されます。どこにも送信されません。レポートは自分で貼り付けた場合にのみPCの外へ出ます。記録した出来事にはキャラクター名が含まれるため、共有する前に内容を確認してください。",
        },
        ["copied"] = new[] { "Copied to the clipboard.", "Copié dans le presse-papiers.", "In die Zwischenablage kopiert.", "クリップボードにコピーしました。" },

        // about page
        ["version_line"] = new[] { "Version {VER} · Dalamud plugin for {PVP}", "Version {VER} · plugin Dalamud pour le {PVP}", "Version {VER} · Dalamud-Plugin für {PVP}", "バージョン {VER}・{PVP}用のDalamudプラグイン" },
        ["about_desc"] = new[]
        {
            "Jarvis PvP gives every player in {PVP} a clear plate: {HP}, {MP} and {LB} bars, job icon, {GUARD} with its recast, debuffs and signs, and it shows which enemies have you as their target. Like a personal assistant in the match, it only shows what the game already knows and never acts for you.",
            "Jarvis PvP donne à chaque joueur en {PVP} une barre claire : {HP}, {PM} et {LB}, icône de job, {GUARD} et sa recharge, effets néfastes et signes de ciblage, et il indique quels ennemis vous ont pour cible. Tel un assistant personnel pendant le match, il n'affiche que ce que le jeu sait déjà et n'agit jamais à votre place.",
            "Jarvis PvP gibt jedem Spieler im {PVP} eine klare Leiste: {HP}, {MP} und {LB}, Job-Symbol, {GUARD} mit Reaktivierung, negative Status und Feindmarkierungen, und es zeigt, welche Gegner dich als Ziel haben. Wie ein persönlicher Assistent im Match zeigt es nur, was das Spiel ohnehin weiß, und handelt nie für dich.",
            "Jarvis PvPは、{PVP}のすべてのプレイヤーに見やすいバーを表示します：{HP}・{MP}・{LB}、ジョブアイコン、{GUARD}とそのリキャスト、デバフ、ターゲットマーカー。さらに、どの敵が自分をターゲットしているかも分かります。試合中のパーソナルアシスタントのように、ゲームがすでに持っている情報だけを表示し、操作を代行することはありません。",
        },
        ["sec_support"] = new[] { "Support", "Soutien", "Unterstützung", "支援" },
        ["support_boosty"] = new[] { "Support me on Boosty", "Me soutenir sur Boosty", "Unterstütze mich auf Boosty", "Boostyで支援する" },
        ["sec_shortcuts"] = new[] { "Shortcuts", "Raccourcis", "Befehle", "コマンド" },
        ["cmd_open"] = new[] { "Open or close these settings", "Ouvre ou ferme ces réglages", "Öffnet oder schließt diese Einstellungen", "この設定画面を開く／閉じる" },
        ["cmd_toggle"] = new[] { "Turn the plates on, off or switch them", "Active, désactive ou bascule les barres", "Schaltet die Leisten ein, aus oder um", "バーをオン・オフ・切り替え" },
        ["sec_credits"] = new[] { "Credits", "Crédits", "Mitwirkende", "クレジット" },
        ["cr_original"] = new[]
        {
            "Original plugin: PvP TopPlates by Petra105 (MIT licence). Jarvis PvP is built on its code.",
            "Plugin d'origine : PvP TopPlates de Petra105 (licence MIT). Jarvis PvP est construit sur son code.",
            "Ursprüngliches Plugin: PvP TopPlates von Petra105 (MIT-Lizenz). Jarvis PvP baut auf dessen Code auf.",
            "元のプラグイン：Petra105氏の「PvP TopPlates」（MITライセンス）。Jarvis PvPはそのコードを基にしています。",
        },
        ["cr_ai"] = new[]
        {
            "Made with the help of the AI Claude (Anthropic): ideas and the creation of features.",
            "Réalisé avec l'aide de l'IA Claude (Anthropic) : idées et création des fonctionnalités.",
            "Entstanden mit Hilfe der KI Claude (Anthropic): Ideen und Umsetzung der Funktionen.",
            "AI「Claude」（Anthropic）の協力で制作：アイデアと機能の作成。",
        },
        ["cr_thanks"] = new[]
        {
            "Thanks to the Dalamud and FFXIVClientStructs teams, whose tools make plugins like this possible.",
            "Merci aux équipes de Dalamud et de FFXIVClientStructs, dont les outils rendent possibles des plugins comme celui-ci.",
            "Danke an die Teams von Dalamud und FFXIVClientStructs, deren Werkzeuge Plugins wie dieses möglich machen.",
            "このようなプラグインを可能にしているDalamudとFFXIVClientStructsの開発チームに感謝します。",
        },
        ["cr_assets"] = new[]
        {
            "FINAL FANTASY XIV © SQUARE ENIX. Icons are read from the game's own files. This plugin is not affiliated with or endorsed by Square Enix.",
            "FINAL FANTASY XIV © SQUARE ENIX. Les icônes sont lues dans les fichiers du jeu. Ce plugin n'est ni affilié à Square Enix ni approuvé par Square Enix.",
            "FINAL FANTASY XIV © SQUARE ENIX. Die Symbole werden aus den Spieldateien gelesen. Dieses Plugin steht in keiner Verbindung zu Square Enix und wird nicht von Square Enix unterstützt.",
            "FINAL FANTASY XIV © SQUARE ENIX。アイコンはゲーム内のファイルから読み込んでいます。本プラグインはスクウェア・エニックスとは無関係であり、公認を受けたものでもありません。",
        },

        // sharing settings
        ["sec_share"] = new[] { "Share settings", "Partager les réglages", "Einstellungen teilen", "設定の共有" },
        ["share_help"] = new[]
        {
            "Copy a settings code and send it to a friend, or paste a code someone shared with you. The code holds every plate setting; the language, theme and layout of this window are not included.",
            "Copiez un code de réglages et envoyez-le à un ami, ou collez un code qu'on vous a partagé. Le code contient tous les réglages des barres ; la langue, le thème et la disposition de cette fenêtre n'en font pas partie.",
            "Kopiere einen Einstellungscode und schick ihn einem Freund, oder füge einen Code ein, den jemand mit dir geteilt hat. Der Code enthält alle Einstellungen der Leisten; Sprache, Design und Layout dieses Fensters gehören nicht dazu.",
            "設定コードをコピーして友達に送ったり、共有されたコードを貼り付けたりできます。コードにはバーの設定がすべて含まれます。このウィンドウの言語・テーマ・レイアウトは含まれません。",
        },
        ["export_code"] = new[] { "Copy settings code", "Copier le code des réglages", "Einstellungscode kopieren", "設定コードをコピー" },
        ["import_code"] = new[] { "Import from clipboard", "Importer depuis le presse-papiers", "Aus Zwischenablage importieren", "クリップボードから読み込む" },
        ["import_help"] = new[]
        {
            "Copy a code (it starts with JPVP1:) and press this button. Your current settings are replaced; Undo import brings them back.",
            "Copiez un code (il commence par JPVP1:) puis appuyez sur ce bouton. Vos réglages actuels sont remplacés ; « Annuler l'import » les rétablit.",
            "Kopiere einen Code (er beginnt mit JPVP1:) und drücke diesen Knopf. Deine aktuellen Einstellungen werden ersetzt; »Import rückgängig« stellt sie wieder her.",
            "コード（JPVP1: で始まります）をコピーしてからこのボタンを押してください。現在の設定は置き換えられます。「読み込みを取り消す」で元に戻せます。",
        },
        ["undo_import"] = new[] { "Undo import", "Annuler l'import", "Import rückgängig", "読み込みを取り消す" },
        ["import_done"] = new[] { "Settings imported.", "Réglages importés.", "Einstellungen importiert.", "設定を読み込みました。" },
        ["import_failed"] = new[] { "No Jarvis PvP settings code in the clipboard.", "Aucun code de réglages Jarvis PvP dans le presse-papiers.", "Kein Einstellungscode von Jarvis PvP in der Zwischenablage.", "クリップボードにJarvis PvPの設定コードがありません。" },
        ["import_undone"] = new[] { "Previous settings restored.", "Réglages précédents rétablis.", "Vorherige Einstellungen wiederhergestellt.", "前の設定に戻しました。" },

        // preview scene and "Test in world" plate texts
        ["preview_enemy"] = new[] { "Enemy Player", "Joueur ennemi", "Gegnerischer Spieler", "敵プレイヤー" },
        ["preview_ally"] = new[] { "Ally Player", "Joueur allié", "Verbündeter Spieler", "味方プレイヤー" },
        ["pv_you"] = new[] { "You", "Vous", "Du", "自分" },
        ["pv_target"] = new[] { "Target", "Cible", "Ziel", "ターゲット" },
        ["pv_ally"] = new[] { "Ally", "Allié", "Verbündeter", "味方" },
        ["pv_off"] = new[] { "hidden", "masqué", "ausgeblendet", "非表示" },
    };

    private static readonly Dictionary<(int Language, uint Id), string> ActionNames = new();
    private static readonly Dictionary<(int Language, uint Id), string> ParamNames = new();

    public static int Selected { get; set; }

    /// <summary>0 English, 1 French, 2 German, 3 Japanese, 4 Russian (not a game language: game terms stay English).</summary>
    public static int Index
    {
        get
        {
            if (Selected == 5)
                return 4;

            var language = Selected switch
            {
                1 => ClientLanguage.English,
                2 => ClientLanguage.French,
                3 => ClientLanguage.German,
                4 => ClientLanguage.Japanese,
                _ => Plugin.ClientState.ClientLanguage,
            };
            return language switch
            {
                ClientLanguage.French => 1,
                ClientLanguage.German => 2,
                ClientLanguage.Japanese => 3,
                _ => 0,
            };
        }
    }

    public static string T(string key)
    {
        if (!Texts.TryGetValue(key, out var values))
            return key;

        var index = Index;
        if (index == 4)
            return Fill(Russian.TryGetValue(key, out var russian) ? russian : values[0], index);
        return Fill(values[index], index);
    }

    /// <summary>Text drawn on a plate with the game font: English instead of Russian (the game font has no Cyrillic).</summary>
    public static string PlateText(string key)
    {
        var index = Index;
        return index == 4 && Texts.TryGetValue(key, out var values) ? Fill(values[0], index) : T(key);
    }

    /// <summary>Text with a stable ImGui id (the visible text can change with the language, the id does not).</summary>
    public static string L(string key, string id)
    {
        return $"{T(key)}###{id}";
    }

    public static string Unit(string unit)
    {
        var index = Term(Index);
        return unit switch
        {
            "yalms" => new[] { "yalms", "yalms", "Yalm", "ヤルム" }[index],
            "per_second" => new[] { "/s", "/s", "/s", "/秒" }[index],
            _ => unit,
        };
    }

    /// <summary>HP in the short form of the language: k (with the language's decimal mark), 万 in Japanese.</summary>
    public static string ShortNumber(uint value)
    {
        var index = Index;
        if (index == 3)
        {
            return value >= 10_000
                ? (value / 10_000f).ToString(value >= 1_000_000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "万"
                : value.ToString("0", CultureInfo.InvariantCulture);
        }

        if (value < 1_000)
            return value.ToString("0", CultureInfo.InvariantCulture);

        var culture = index is 1 or 2 ? CultureInfo.GetCultureInfo(index == 1 ? "fr-FR" : "de-DE") : CultureInfo.InvariantCulture;
        return (value / 1000f).ToString(value >= 100_000 ? "0" : "0.#", culture) + "k";
    }

    /// <summary>Minutes suffix of debuff timers.</summary>
    public static string Minutes(float seconds)
    {
        var minutes = MathF.Ceiling(seconds / 60f).ToString("0", CultureInfo.InvariantCulture);
        return Index == 3 ? minutes + "分" : minutes + "m";
    }

    private static string Fill(string text, int index)
    {
        if (text.IndexOf('{') < 0)
            return text;

        index = Term(index);

        return text
            .Replace("{GUARD}", ActionName(29054, index, new[] { "Guard", "Garde", "Wehr", "防御" }))
            .Replace("{ELIXIR}", ActionName(29055, index, new[] { "Standard-issue Elixir", "Élixir militaire", "Frontenelixier", "軍用エリクサー" }))
            .Replace("{HP}", ParamName(7, index, new[] { "HP", "PV", "LP", "HP" }))
            .Replace("{MP}", ParamName(8, index, new[] { "MP", "PM", "MP", "MP" }))
            .Replace("{PM}", ParamName(8, index, new[] { "MP", "PM", "MP", "MP" }))
            .Replace("{LB}", new[] { "Limit Break", "Transcendance", "Limitrausch", "リミットブレイク" }[index])
            .Replace("{CC}", new[] { "Crystalline Conflict", "Crystalline Conflict", "Crystalline Conflict", "クリスタルコンフリクト" }[index])
            .Replace("{PVP}", new[] { "PvP", "JcJ", "PvP", "PvP" }[index])
            .Replace("{SIGNS}", new[] { "Signs", "Signes de ciblage", "Feindmarkierungen", "ターゲットマーカー" }[index])
            .Replace("{PARTYLIST}", new[] { "Party list", "Liste des équipiers", "Gruppenliste", "パーティリスト" }[index])
            .Replace("{TANK}", new[] { "Tank", "Tank", "Verteidiger", "タンク" }[index])
            .Replace("{HEALER}", new[] { "Healer", "Soigneur", "Heiler", "ヒーラー" }[index])
            .Replace("{DPS}", new[] { "DPS", "DPS", "Angreifer", "DPS" }[index]);
    }

    /// <summary>Language of game terms: Russian keeps the English names of abilities, gauges and modes.</summary>
    private static int Term(int index) => index is >= 0 and <= 3 ? index : 0;

    private static ClientLanguage ToClientLanguage(int index)
    {
        return index switch
        {
            1 => ClientLanguage.French,
            2 => ClientLanguage.German,
            3 => ClientLanguage.Japanese,
            _ => ClientLanguage.English,
        };
    }

    private static string ActionName(uint id, int index, string[] fallback)
    {
        if (ActionNames.TryGetValue((index, id), out var cached))
            return cached;

        var name = fallback[index];
        try
        {
            var row = Plugin.DataManager.GetExcelSheet<LuminaAction>(ToClientLanguage(index)).GetRowOrDefault(id);
            var text = row?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(text))
                name = text;
        }
        catch (Exception)
        {
            // keep the verified fallback
        }

        ActionNames[(index, id)] = name;
        return name;
    }

    private static string ParamName(uint id, int index, string[] fallback)
    {
        if (ParamNames.TryGetValue((index, id), out var cached))
            return cached;

        var name = fallback[index];
        try
        {
            var row = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BaseParam>(ToClientLanguage(index)).GetRowOrDefault(id);
            var text = row?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(text))
                name = text;
        }
        catch (Exception)
        {
            // keep the verified fallback
        }

        ParamNames[(index, id)] = name;
        return name;
    }
}
