   M S B A M L   `   `   `  ����  OrganizadorArquivosWPF1    +OrganizadorArquivosWPF.Views.FallbackWindow   5   	   W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35D 9http://schemas.microsoft.com/winfx/2006/xaml/presentation    5   	   R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	     � Title$  Dados Manuais��5   	   $	��355��5   	   $	��332��6    � WindowStartupLocation$ CenterOwner=�5   	   ���#F2F2F2 � 	Resources 5	         	CardStyle �� 
TargetType �� Property �� Value3  -System.Windows.Media.Effects.DropShadowEffect   Color   Opacity   
BlurRadius	   ShadowDepth  BottomBorderTextBox
 �� 
TargetType �� CornerRadius ��Name  RoundedButtonTemplate �� Padding  PrimaryButton  	DevButton �� BasedOn��%�  &	        &	 S    &	     &	 �    7  &	     7  �� 5      " ��6   .�5      ��  � ��
 �����6$   �� 5       �@��CornerRadius$ 8��6$   �� 5       �@��Padding$ 20q�6$   �� 5       � ��$ 10q�6$   �� 5       �@J�Effect 5        5      $ Black��6   $
 0.15Q�5   '   $ 15Q�5   '   $	 2Q�5   '   5      5      �� 5      " ��6&   .�5      ��  � ��
 ���� 6'   �� 5       � �� �#007ACC6'   �� 5       � ��$ 0,0,0,2q�6'   �� 5       � ��$ 4q�6'   �� 5        � ��$ 16(�6'   �� 5!       � �� 5"      �� 5#      "
 ��6   .R�5$      �� #��z��6   #��z��5%   !   #��z��5&   !   $ 0,0,4,4��5'   !   ._�5(   	   �� $ PART_ContentHost��6   5)   	   5*      5,      5-      �� 50      "
 ��62   .R�51      �� #��z��6   $ 5��52      # z��53      ._�54      �� $��Center=�6   $��Center=�55   #   56      57      �� 59      " ��6    .�5:      ��  � ��8   6&   �� 5;       � �� �#007ACC6&   �� 5<       � ��
 �����6&   �� 5=       � ��$	 8,4q�6&   �� 5>       � ��$
 Bold"�6&   �� 5?       �@�Cursor$
 Hand��6&   �� 5@       � ��$ 0q�6'   �� 5A       � ��$ 75��6&   5B      �� 5D      " ��6   8   .�5E      ��  � �� �#CCCCCC6"   �� 5F       � �� �#3333336"   5G      5H      .��5K      �� #��[  6
   $��Center=�5L      $��Center=�5M      $	��292��6*   $	��254��66   ._�5N      �  � RowDefinitions 5O      �� 5P      $
��Auto �6   �� 5R      $
��Auto �6   �� 5T      $
��Auto �6   �� 5V      $
��Auto �6   �� 5X      $
��Auto �6    � ColumnDefinitions 5[      �� 5\      $
��Auto �6   �� 5]      $��* �6   .+�5a      �� $	��OS:��6   $��0��6   $��0��6'   $��Center=�5b      �� 5c      -   5         ��Name$ 	TxtOSFull��5c      $��0��5d      $��1��6#   #��[ 5e       �� 
IsReadOnly . $��	10,0,0,10q�5g      �� 5j      $��Rota:��6   $��1��6   $��0��6)   $��Center=�5k      �� 5l      $��1��6   $��1��6   ���#007ACC$��0,0,0,2q�5n      $ 0,0,4,4��5o      
������ $��	10,0,0,10q�5q      $	��100��5r      ._�5s      �� -   5         ��Name$ CmbRota��5s      
������ $��0q�5u      $��4q�5v      5w      5x      �� 5{      $��
ID SIGFI:��6   $��2��6    $��0��6-   $��Center=�5|      �� 5}      -   5        $ 
TxtIdSigfi��5}      $��2��5~      $��1��6#   #��[ 5      $��	10,0,0,10q�5�      5�      �� 5�      -   5         ��Name$ Chk160��5�      $��3��5�      $��2��6$   $%��Sistema 160 (2 controladores)?��5�      $��0,10,0,0q�5�      5�      �� 5�      $��4��6   $��2��6    �� Orientation$ 
Horizontal=�5�      $��Right=�5�      $��0,20,0,0q�5�      .+�5�      �� -   5         ��Name$ BtnOk��5�      $��OK��5�      #��[ 5�      {�.  5�      �� 5�      -   5        $��Cancelar��5�      #��[ 5�      $��10,0,0,0q�5�      5�      5�      5�      5�      5�      