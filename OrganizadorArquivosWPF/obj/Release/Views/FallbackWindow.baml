   M S B A M L   `   `   `  ����   OrganizadorArquivosWPF1    +OrganizadorArquivosWPF.Views.FallbackWindow   W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35D 9http://schemas.microsoft.com/winfx/2006/xaml/presentation    R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml       � Title$  Dados Manuais��$	��360��$	��456�� � WindowStartupLocation$ CenterOwner=����#F2F2F2 � 	Resources    	CardStyle �� 
TargetType �� Property �� Value3  -System.Windows.Media.Effects.DropShadowEffect   Color   Opacity   
BlurRadius	   ShadowDepth  BottomBorderTextBox
 �� 
TargetType �� CornerRadius ��Name  RoundedButtonTemplate �� Padding  PrimaryButton  DangerButton �� BasedOn��%�  &	        &	 �     &	 �    &	 �    7  &	 �    7  �� " ��.���  � ��
 �������  �@��CornerRadius$ 8����  �@��Padding$ 20q���  � ��$ 10q���  �@J�Effect   $ Black��$
 0.15Q�$ 15Q�$	 2Q��� " ��.���  � ��
 ���� ��  � �� �#007ACC��  � ��$ 0,0,0,2q���  � ��$ 4q���  � �� �� "
 ��.R��� #��z��#��z��#��z��$ 0,0,4,4��._��� $ PART_ContentHost���� "
 ��.R��� #��z��$ 5��# z��._��� $��Center=�$��Center=��� " ��.���  � ��8   ��  � �� �#007ACC��  � ��
 �������  � ��$
 10,6q���  � ��$
 Bold"���  �@�Cursor$
 Hand����  � ��$ 0q��� " ��8   .���  � �� �#E81123.��� $��0,0,0,28q�.+��� #��[  $��10,30,10,28q�._��� .+��� $��Número da OS:��$
��Bold"�$��0,0,0,6q��� -    ��Name$ 	TxtOSFull��#��[ $��18(� �� 
IsReadOnly . �� $��	ID SIGFI:��$
��Bold"�$��0,12,0,6q��� -   $ 
TxtIdSigfi��#��[ $��18(��� $��Rota:��$
��Bold"�$��0,12,0,6q��� ���#007ACC$��0,0,0,2q�$ 0,0,4,4��
������ ._��� -    ��Name$ CmbRota��
������ $��0q�$��4q�$	��297�� �� 
IsEditable .  .@��� $��— selecione —���� -    ��Name$ Chk160��$%��Sistema 160 (2 controladores)?��$��0,12,0,0q���  �� Orientation$ 
Horizontal=�$��Right=�$��0,20,0,0q�.+��� -    ��Name$ BtnOk��$��OK��#��[ $��80��{�.  �� -   $��Cancelar��#��[ $��80��$��10,0,0,0q�