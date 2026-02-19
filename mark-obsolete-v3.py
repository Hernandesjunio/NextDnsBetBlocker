#!/usr/bin/env python3
"""
Script para marcar componentes não utilizados como [Obsolete]
Versão 3 - com busca correta de padrões
"""

import os

def add_obsolete_to_interface(file_path, interface_name, message):
    """Adiciona [Obsolete] diretamente antes da interface"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # Procura pela interface sem [Obsolete]
        pattern = f'public interface {interface_name}\n'
        if pattern in content and f'[Obsolete' not in content[:content.find(pattern)+100]:
            # Substitui com a versão com [Obsolete]
            new_content = content.replace(
                pattern,
                f'[Obsolete("{message}", false)]\npublic interface {interface_name}\n'
            )
            
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(new_content)
            print(f"✓ Marcado {interface_name} como [Obsolete]")
            return True
        else:
            print(f"✓ {interface_name} já está marcado ou não encontrado")
            return False
            
    except Exception as e:
        print(f"✗ Erro ao processar {interface_name}: {e}")
        return False


def add_obsolete_to_class(file_path, class_name, message):
    """Adiciona [Obsolete] diretamente antes da classe"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # Procura pela classe sem [Obsolete]
        pattern = f'public class {class_name}'
        if pattern in content and not (f'[Obsolete' in content[:content.find(pattern)+100]):
            # Substitui com a versão com [Obsolete]
            new_content = content.replace(
                pattern,
                f'[Obsolete("{message}", false)]\npublic class {class_name}'
            )
            
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(new_content)
            print(f"✓ Marcado {class_name} como [Obsolete]")
            return True
        else:
            print(f"✓ {class_name} já está marcado ou não encontrado")
            return False
            
    except Exception as e:
        print(f"✗ Erro ao processar {class_name}: {e}")
        return False


def main():
    repo_path = r"C:\Users\herna\source\repos\DnsBlocker"
    base_path = os.path.join(repo_path, r"src\NextDnsBetBlocker.Core")
    
    print("═" * 70)
    print("  Marcando componentes não utilizados como [Obsolete]")
    print("═" * 70)
    print()
    
    # INTERFACES
    print("Marcando INTERFACES...")
    print()
    
    add_obsolete_to_interface(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "INextDnsClient",
        "This interface is not used in the current implementation. Use ILogsProducer instead."
    )
    
    add_obsolete_to_interface(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "ICheckpointStore",
        "This interface is not used in the current implementation."
    )
    
    add_obsolete_to_interface(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "IBlockedDomainStore",
        "This interface is not used in the current implementation."
    )
    
    add_obsolete_to_interface(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "IGamblingSuspectAnalyzer",
        "This interface is not used in the current implementation."
    )
    
    print()
    print("═" * 70)
    print("  ✓ Processo concluído!")
    print("═" * 70)


if __name__ == "__main__":
    main()
