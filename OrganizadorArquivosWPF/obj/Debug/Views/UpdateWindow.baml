   M S B A M L   `   `   `  ����  OrganizadorArquivosWPF/    )OrganizadorArquivosWPF.Views.UpdateWindow   5   	   W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35D 9http://schemas.microsoft.com/winfx/2006/xaml/presentation    5   	   R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	     � Title$  Atualizando Aplicativo…��5   	   $	��420��5   	   $	��180��6    � WindowStartupLocation$ CenterOwner=�5   	    � 
ResizeMode$ NoResize=�5   	    � WindowStyle$
 None=�5   	    � ShowInTaskbar .   � Topmost . ���#F2F2F2 � 	Resources 5         	CardStyle �� 
TargetType �� Property	 �� Value3  -System.Windows.Media.Effects.DropShadowEffect
   Color   Opacity   
BlurRadius   ShadowDepth��%]  &	        �� 5   
   " ��6"   .�5      ��  � ��
	 �����6-   �� 5       �@��CornerRadius$	 8��6-   �� 5       �@��Padding$	 16q�6-   �� 5       � ��$	 10q�6-   �� 5       �@J�Effect	 5        5      $
 Black��6'   $
 0.15Q�5   '   $ 12Q�5   '   $ 2Q�5   '   5      5      5      .��5!      � .+�5#   
   �� #��[  6   $��Center=�5$      $��Center=�5%      $	��360��5&      ._�5'      �� .+�5)      �� -   5         ��Name$ 	TxtStatus��5)      $ ��Iniciando atualização…��5*      $��14(�5+      $
��Bold"�5,      $
��Wrap=�5-       �� TextAlignment$ Center=�5.      $��0,0,0,12q�5/      �� 52       �� CornerRadius$ 4��6   ���#E0E0E0$��12��54      $��0,0,0,6q�55      ._�56      � -   5         �Name$ PrgDeterminate��56   "   
������ ���#007ACC$��12��59   "    � Minimum$ 0Q�5:   "    � Maximum$	 100Q�5;   "    � Value$ 0Q�5<   "   5=      �� 5@      -   5         ��Name$ IndeterminateContainer��5E      $ 4��5@      ���#E0E0E0$��12��5B      $��0,0,0,12q�5C      $y�	Collapsed=�5D      ._�5F      � -   5        $ PrgIndeterminate��5F   "    � IsIndeterminate . 
������ ���#007ACC$��12��5J   "   5K      �� 5N      $2��,Por favor, aguarde. Não feche o aplicativo.��6   $��12(�5O      $
��Wrap=�5P      $ Center=�5Q      
�������5R      5S      5T      5U      5V      