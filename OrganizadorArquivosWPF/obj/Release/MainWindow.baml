   M S B A M L   `   `   `  ����   OrganizadorArquivosWPF'    !OrganizadorArquivosWPF.MainWindow   W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35D 9http://schemas.microsoft.com/winfx/2006/xaml/presentation    R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml       � Title$5  /One Engenharia LTDA – Organizador de Arquivos��$	��900��$	��600��$	��800��$	��500�� � WindowStartupLocation$ CenterScreen=����#F2F2F2 � 	Resources    	CardStyle �� 
TargetType �� Property �� Value3  -System.Windows.Media.Effects.DropShadowEffect   Color   Opacity   
BlurRadius	   ShadowDepth  BottomBorderTextBox
 �� 
TargetType �� CornerRadius ��Name  RoundedButtonTemplate �� Padding  PrimaryButton  DangerButton �� BasedOn  	DevButton  SuccessButton �� Triggers P� Property P� Value=  7System.Windows.Controls.Primitives.DataGridColumnHeader  CellCenterBlack  LogRowStyle)  #System.Windows.Controls.DataGridRow �� Binding �� Value��%�  &	        &	 �     &	 �    &	 �    7  &	 �    7  &	 �    7  &	     7  '  a    &	 �    &	 t    �� " ��.���  � ��
 �������  �@��CornerRadius$ 8����  �@��Padding$ 20q���  � ��$ 10q���  �@J�Effect   $ Black��$
 0.15Q�$ 15Q�$	 2Q��� " ��.���  � ��
 ���� ��  � �� �#007ACC��  � ��$ 0,0,0,2q���  � ��$ 4q���  � �� �� "
 ��.R��� #��z��#��z��#��z��$ 0,0,4,4��._��� $ PART_ContentHost���� "
 ��.R��� #��z��$ 5��# z��._��� $��Center=�$��Center=��� " ��.���  � ��8   ��  � �� �#007ACC��  � ��
 �������  � ��$
 10,6q���  � ��$
 Bold"���  �@�Cursor$
 Hand����  � ��$ 0q��� " ��8   .���  � �� �#E81123�� " ��8   .���  � �� �#CCCCCC��  � �� �#333333�� " ��8   .���  � �� �#59c130 P�  �@J�IsMouseOver . �� "  .���  � �� �#E0E0E0��  � ��
 �   ���  � ��$
 Bold"���  � ��$ Center=���  � ��$	 8,4q��� " ��.���  � ��$ Center=���  � ��$ Center=���  � ��$
 Bold"���  � ��$
 Wrap=���  � ��
 �   ��� "  .���  � ��
 �������  � ��
 �   ���  � ��$ 28�� ��  �� *Tipo+ Info.a���  � �� �#EEFFEE��  �� *Tipo+ Warn.a���  � �� �#FFF7D9��  �� *Tipo+	 Error.a���  � �� �#FFE8E8.���  � ColumnDefinitions �� $	��200 ��� $��* � � RowDefinitions �� $��* ��� $
��Auto �.+��� $��0��$��0�����#007ACC$ 20q�$��2��._��� .+��� $��logo.png��$	��100��$��Center=�$��0,0,0,20q��� $��suporte@oneengenharia.com��
�������$��0,0,0,10q��� -    ��Name$ BtnCheckUpdate��$��Procurar Atualização��#��[ $��0,0,0,10q��� -   $ BtnExportar��$��Exportar Log��#��[ $��0,0,0,20q��� $��© 2025 One Engenharia��
�������� $��0��$��1��$��0,0,10,0q� �� $
��Auto ��� $��* �.+��� $��0��#��[  ._��� .+��� $��Pasta de Origem:��$
��Bold"�$��0,0,0,6q�]� $��0,0,0,12q�.+��� -    ��Name$ TxtPasta��#��[  �� 
IsReadOnly . $	��300��$	��508���� -   $ BtnSelecionar��$��
Selecionar��#��[ $	��100��$��0,0,0,0q�$��Right=�$��38����  �� Orientation$ 
Horizontal=�.+��� $��0,0,24,0q�.+��� $��Nº OS:��$
��Bold"�$��0,0,0,6q��� -   $ TxtOS��#��[ $	��150��$��20(�$	��298���� .+��� $	��UF:��$
��Bold"�$��0,0,0,6q��� ���#007ACC$��0,0,0,2q�$ 0,0,4,4��._��� -    ��Name$ CmbUF��
������ $��0q�$��8q�$��34��$��60�� �� SelectedIndex$ 0��.@��� $��AC���� $��MT���� $��Right=�$ 
Horizontal=�.+��� -   $ BtnProcessar��$��	Processar��#��[ $��98��$��	20,20,8,0q� �� 	IsDefault . �� -   $ BtnDesfazer��$��Abrir Destino��#��[ $��98��$��	10,20,8,0q�$y�	Collapsed=�� -	    �Name$ Progress��$��20��$��0,12,0,0q� � IsIndeterminate . $y�	Collapsed=��� $��1��#��[  ._��  �� $
��Auto ��� $��* �.+��� $��0��$��Log de Operações:��$
��Bold"�$��0,0,0,8q�&   System.Windows.Controls.DataGrid  -
     Name$ GridLog��$��1��    AutoGenerateColumns  .  !   
IsReadOnly! . "   RowStyle#" [ #   GridLinesVisibility$
# None=�$   HeadersVisibility$$ Column=�$
��Auto=�$
��Auto=�$��12(�%   CanUserResizeRows% .  &   Columns& 0  *System.Windows.Controls.DataGridTextColumn  ,  &System.Windows.Controls.DataGridColumn'   Header$
' Hora��(   Width5  /System.Windows.Controls.DataGridLengthConverter$	( 100 1  +System.Windows.Controls.DataGridBoundColumn)   Binding) �� *Hora+* �� StringFormat$* HH:mm:ss��+   ElementStyle+ �� " ��# [   $
' Tipo��$( 90 ) �� *Tipo++ �� " ��# [   $' Mensagem��$( * ) �� *
Mensagem++ �� " ��# [ .���  �@��TextAlignment$ Center=�]� $��1��$��1��$
��10,5q���.  .+��� -   , ��Name$, 	LblVersao��$
��Left=�$��Center=�
�������$��Italic$�