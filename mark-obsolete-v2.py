#!/usr/bin/env python3
"""
Script para marcar componentes não utilizados como [Obsolete]
Versão 2 - com tratamento correto de padrões
"""

import os
import re

def add_obsolete_attribute_to_interface(file_path, interface_name, message):
    """Adiciona atributo [Obsolete] à interface"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        # Encontra a linha da interface
        for i, line in enumerate(lines):
            if f'public interface {interface_name}' in line:
                # Verifica se a linha anterior é um comentário
                if i > 0 and '/// </summary>' in lines[i-1]:
                    # Adiciona [Obsolete] antes de public interface
                    indent = len(line) - len(line.lstrip())
                    obsolete_line = ' ' * indent + f'[Obsolete("{message}", false)]\n'
                    lines.insert(i, obsolete_line)
                    
                    with open(file_path, 'w', encoding='utf-8') as f:
                        f.writelines(lines)
                    print(f"✓ Marcado {interface_name} como [Obsolete]")
                    return True
        
        print(f"✗ Interface {interface_name} não encontrada em {file_path}")
        return False
    except Exception as e:
        print(f"✗ Erro ao processar {interface_name}: {e}")
        return False


def add_obsolete_attribute_to_class(file_path, class_name, message):
    """Adiciona atributo [Obsolete] à classe"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        # Encontra a linha da classe
        for i, line in enumerate(lines):
            if f'public class {class_name}' in line and '[Obsolete' not in line:
                # Verifica se a linha anterior é um comentário
                if i > 0 and '/// </summary>' in lines[i-1]:
                    # Adiciona [Obsolete] antes de public class
                    indent = len(line) - len(line.lstrip())
                    obsolete_line = ' ' * indent + f'[Obsolete("{message}", false)]\n'
                    lines.insert(i, obsolete_line)
                    
                    with open(file_path, 'w', encoding='utf-8') as f:
                        f.writelines(lines)
                    print(f"✓ Marcado {class_name} como [Obsolete]")
                    return True
        
        print(f"✗ Classe {class_name} não encontrada em {file_path}")
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
    
    add_obsolete_attribute_to_interface(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "INextDnsClient",
        "This interface is not used in the current implementation. Use ILogsProducer instead."
    )
    
    add_obsolete_attribute_to_interface(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "ICheckpointStore",
        "This interface is not used in the current implementation."
    )
    
    add_obsolete_attribute_to_interface(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "IBlockedDomainStore",
        "This interface is not used in the current implementation."
    )
    
    add_obsolete_attribute_to_interface(
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
