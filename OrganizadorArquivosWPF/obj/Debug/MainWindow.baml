   M S B A M L   `   `   `  ����  OrganizadorArquivosWPF'    !OrganizadorArquivosWPF.MainWindow   5   	   W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35D 9http://schemas.microsoft.com/winfx/2006/xaml/presentation    5   	   R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	     � Title$5  /One Engenharia LTDA – Organizador de Arquivos��5   	   $	��900��5   	   $	��600��6   $	��800��5   	   $	��500��6    � WindowStartupLocation$ CenterScreen=�5   	   ���#F2F2F2 � 	Resources 5         	CardStyle �� 
TargetType �� Property �� Value3  -System.Windows.Media.Effects.DropShadowEffect   Color   Opacity   
BlurRadius	   ShadowDepth  BottomBorderTextBox
 �� 
TargetType �� CornerRadius ��Name  RoundedButtonTemplate �� Padding  PrimaryButton  DangerButton �� BasedOn  	DevButton��%�  &	        &	 S    &	 �    &	 �    7  &	 �    7  &	     7  �� 5   
   " ��6"   .�5      ��  � ��
 �����6-   �� 5       �@��CornerRadius$ 8��6-   �� 5       �@��Padding$ 20q�6-   �� 5       � ��$ 10q�6-   �� 5       �@J�Effect 5        5      $ Black��6'   $
 0.15Q�5   '   $ 15Q�5   '   $	 2Q�5   '   5      5      �� 5   
   " ��6,   .�5      ��  � ��
 ���� 60   �� 5       � �� �#007ACC60   �� 5        � ��$ 0,0,0,2q�60   �� 5!       � ��$ 4q�60   �� 5"       � �� 5#      �� 5$      "
 ��6&   .R�5%      �� #��z��6!   #��z��5&   !   #��z��5'   !   $ 0,0,4,4��5(   !   ._�5)      �� $ PART_ContentHost��6+   5*      5+      5-      5.      �� 51   
   "
 ��68   .R�52      �� #��z��6   $ 5��53      # z��54      ._�55      �� $��Center=�6#   $��Center=�56   #   57      58      �� 59   
   " ��6&   .�5:      ��  � ��8   6/   �� 5;       � �� �#007ACC6/   �� 5<       � ��
 �����6/   �� 5=       � ��$
 10,6q�6/   �� 5>       � ��$
 Bold"�6/   �� 5?       �@�Cursor$
 Hand��6/   �� 5@       � ��$ 0q�60   5A      �� 5B   
   " ��6%   8   .�5C      ��  � �� �#E811236+   5D      �� 5E   
   " ��6"   8   .�5F      ��  � �� �#CCCCCC6+   �� 5G       � �� �#3333336+   5H      5I      .��5L      �  � ColumnDefinitions 5M   
   �� 5N      $	��200 �6   �� 5P      $��* �6    � RowDefinitions 5S   
   �� 5T      $��* �6   �� 5V      $
��Auto �6   .+�5[   
   �� $��0��6   $��0��6   ���#007ACC$ 20q�5]      $��2��5^      ._�5_      �� .+�5`      �� $��logo.png��6   $	��100��5a      $��Center=�5b      $��0,0,0,20q�5c      �� 5d      $(��"Suporte: suporte@oneengenharia.com��6   
�������$
��Wrap=�5f      $��0,0,0,10q�5g      �� 5h      -   5         ��Name$ BtnCheckUpdate��5h      $��Procurar Atualização��5i      #��[ 5j      $��0,0,0,10q�5k      5l      �� 5m      -   5        $ BtnExportar��5m      $��Exportar Log��5n      #��[ 5o      $��0,0,0,20q�5p      5q      �� 5r      $��© 2025 One Engenharia��6   
�������5s      5t      5u      � 5x   
   $��0��6   $��1��6   $��0,0,10,0q�6,    5y      �� 5z      $
��Auto �6    �� 5|      $��* �6    .+�5�      �� $��0��6   #��[  5�      ._�5�      �� .+�5�      �� $��Pasta de Origem:��6    $
��Bold"�5�       $��0,0,0,6q�5�       ]� 5�      $��0,0,0,12q�6    .+�5�      �� -   5         ��Name$ TxtPasta��5�   "   #��[ 5�   "    �� 
IsReadOnly . $	��300��5�   "   �� 5�      -   5        $ BtnSelecionar��5�   !   $��
Selecionar��5�   !   #��[ 5�   !   $	��100��5�   !   $��8,0,0,0q�5�   !   $��Right=�5�   !   5�      �� 5�       �� Orientation$ 
Horizontal=�6!   .+�5�      �� $��0,0,24,0q�6%   .+�5�      �� $��Nº OS:��6(   $
��Bold"�5�   (   $��0,0,0,6q�5�   (   �� 5�      -   5        $ TxtOS��5�   &   #��[ 5�   &   $	��150��5�   &   $��20(�5�   &   5�      �� 5�      .+�5�      �� $	��UF:��6(   $
��Bold"�5�   (   $��0,0,0,6q�5�   (   �� 5�      ���#007ACC$��0,0,0,2q�5�   %   $ 0,0,4,4��5�   %   
������ ._�5�   "   �� -   5         ��Name$ CmbUF��5�   +   
������ $��0q�5�   +   $��4q�5�   +   $��70��5�   +    �� SelectedIndex$ 0��5�   +   $��28��5�   +   .@�5�   &   �� $��AC��63   �� 5�   &   $��MT��63   5�   #   5�      5�      5�      � 5�      -   5         �Name$ Progress��5�   "   $��20��5�   "   $��0,12,0,0q�5�   "    � IsIndeterminate . $y�	Collapsed=�5�   "   5�      5�      �� 5�      $��1��6   #��[  5�      ._�5�      �  5�      �� 5�      $
��Auto �6(   �� 5�      $��* �6(   .+�5�      �� $��0��6    $��Log de Operações:��5�       $
��Bold"�5�       $��0,0,0,8q�5�       &   System.Windows.Controls.DataGrid  5�      -   5          Name$ GridLog��5�      $��1��5�         AutoGenerateColumns .     
IsReadOnly .    HeadersVisibility$ Column=�5�      $
��Auto=�5�      $
��Auto=�5�      $��11(�5�         CanUserResizeRows .     Columns 5�      0  *System.Windows.Controls.DataGridTextColumn  5�      ,  &System.Windows.Controls.DataGridColumn   Header$
 Hora��61       Width5  /System.Windows.Controls.DataGridLengthConverter$  90 5�   1   1  +System.Windows.Controls.DataGridBoundColumn!   Binding! 5�   1   �� *Hora+" �� StringFormat$" HH:mm:ss��  5�      $
 Tipo��61   $  90 5�   1   ! 5�   1   �� *Tipo+  5�      $ Mensagem��61   $  * 5�   1   ! 5�   1   �� *
Mensagem+#   ElementStyle# 5�   "   �� 5�   &   " ��6,   .�5�   *   ��  � ��$
 Wrap=�6I   5�   '   5�      5�      5�      5�      5�      ]� 5�   
   $��1��6   $��1��6!   $
��10,5q�5�      ��.  .+�5�      �� -	   5        $ ��Name$$ 	LblVersao��5�      $
��Left=�5�      $��Center=�5�      
�������$��Italic$�5�      �� 5�      $��Right=�6   $ 
Horizontal=�5�      .+�5�      �� -
   5        $ BtnProcessar��5�      $��	Processar��5�      #��[ 5�      $	��120��5�      $��0,0,8,0q�5�      % �� 	IsDefault% . 5�      �� 5�      -   5        $ BtnDesfazer��5�      $��Desfazer��5�      #��[ 5      $	��120��5     $y�	Collapsed=�5     5     5     5     5     